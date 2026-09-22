using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteMarkdownSliceReader(SqliteConnectionFactory factory) : IIndexedMarkdownSliceReader
{
    public Task<MarkdownSlice> ReadSliceAsync(
        string path,
        SemanticPointer pointer,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken)
        => ReadSliceCoreAsync(
            path,
            new SemanticAnchor(pointer),
            maxElements,
            maxBytes,
            includeFingerprints: false,
            cancellationToken);

    public Task<MarkdownSlice> ReadSliceAsync(
        string path,
        SemanticAnchor anchor,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken)
        => ReadSliceCoreAsync(
            path,
            anchor,
            maxElements,
            maxBytes,
            includeFingerprints: true,
            cancellationToken);

    private async Task<MarkdownSlice> ReadSliceCoreAsync(
        string path,
        SemanticAnchor anchor,
        int maxElements,
        int maxBytes,
        bool includeFingerprints,
        CancellationToken cancellationToken)
    {
        maxElements = Math.Clamp(maxElements, 1, 100);
        maxBytes = Math.Clamp(maxBytes, 1, 100_000);

        await using var db = factory.Open();
        await using var transaction =
            (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken);

        var documentCommand = db.CreateCommand();
        documentCommand.Transaction = transaction;
        documentCommand.CommandText =
            "select id, source_hash, markdown from documents where path = $path";
        documentCommand.AddParameter("$path", path);

        long documentId;
        string sourceHash;
        string sourceMarkdown;
        await using (var documentReader = await documentCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await documentReader.ReadAsync(cancellationToken))
            {
                throw new SemanticPointerNotFoundException(
                    $"Pointer '{anchor.LogicalPointer.Value}' was not found in '{path}'.");
            }

            documentId = documentReader.GetInt64(0);
            sourceHash = documentReader.GetString(1);
            sourceMarkdown = documentReader.GetString(2);
        }

        var resolvedPointer = anchor.LogicalPointer;
        if (includeFingerprints && anchor.Fingerprint is not null)
        {
            var candidates = await LoadCandidatesAsync(
                db,
                transaction,
                documentId,
                cancellationToken);
            resolvedPointer = SemanticAnchorResolver.Resolve(anchor, candidates);
        }

        var isDocumentRoot =
            SemanticPointerParser.GetKind(resolvedPointer) == SemanticPointerKind.Document;
        var indexedElements = await LoadPageElementsAsync(
            db,
            transaction,
            documentId,
            resolvedPointer,
            isDocumentRoot,
            maxElements + 1,
            cancellationToken);

        if (indexedElements.Count == 0)
        {
            if (!isDocumentRoot)
            {
                throw new SemanticPointerNotFoundException(
                    $"Pointer '{anchor.LogicalPointer.Value}' was not found in '{path}'.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new MarkdownSlice(
                path,
                "document",
                [],
                sourceMarkdown,
                null,
                sourceHash);
        }

        var includedCount = SelectElementCount(
            sourceMarkdown,
            indexedElements,
            isDocumentRoot,
            maxElements,
            maxBytes);
        var includedElements = indexedElements
            .Take(includedCount)
            .Select(element => element.ToSliceElement(includeFingerprints))
            .ToList();
        var nextElement = includedCount < indexedElements.Count
            ? indexedElements[includedCount]
            : null;

        var sourceStart = isDocumentRoot
            ? 0
            : GetReadBoundaryStart(sourceMarkdown, indexedElements[0].SourceStart);
        var sourceEnd = nextElement is null
            ? sourceMarkdown.Length
            : GetReadBoundaryStart(sourceMarkdown, nextElement.SourceStart);
        sourceEnd = Math.Clamp(sourceEnd, sourceStart, sourceMarkdown.Length);

        var responsePointer = isDocumentRoot
            ? "document"
            : includedElements[0].Pointer;
        var nextPointer = nextElement?.ToPublicPointer(includeFingerprints);
        var markdown = sourceMarkdown.Substring(sourceStart, sourceEnd - sourceStart);

        await transaction.CommitAsync(cancellationToken);
        return new MarkdownSlice(
            path,
            responsePointer,
            includedElements,
            markdown,
            nextPointer,
            sourceHash);
    }

    private static int SelectElementCount(
        string sourceMarkdown,
        IReadOnlyList<IndexedSliceElement> indexedElements,
        bool isDocumentRoot,
        int maxElements,
        int maxBytes)
    {
        var sourceStart = isDocumentRoot
            ? 0
            : GetReadBoundaryStart(sourceMarkdown, indexedElements[0].SourceStart);
        var candidateCount = Math.Min(maxElements, indexedElements.Count);
        var includedCount = 0;

        for (var index = 0; index < candidateCount; index++)
        {
            var sourceEnd = index + 1 < indexedElements.Count
                ? GetReadBoundaryStart(
                    sourceMarkdown,
                    indexedElements[index + 1].SourceStart)
                : sourceMarkdown.Length;
            sourceEnd = Math.Clamp(sourceEnd, sourceStart, sourceMarkdown.Length);

            var byteCount = Encoding.UTF8.GetByteCount(
                sourceMarkdown.AsSpan(sourceStart, sourceEnd - sourceStart));
            if (includedCount > 0 && byteCount > maxBytes)
            {
                break;
            }

            includedCount++;
            if (byteCount > maxBytes)
            {
                break;
            }
        }

        return includedCount;
    }

    private static int GetReadBoundaryStart(string sourceMarkdown, int sourceStart)
    {
        sourceStart = Math.Clamp(sourceStart, 0, sourceMarkdown.Length);
        if (sourceStart == 0)
        {
            return 0;
        }

        var previousLineFeed = sourceMarkdown.LastIndexOf('\n', sourceStart - 1);
        return previousLineFeed < 0 ? 0 : previousLineFeed + 1;
    }

    private static async Task<IReadOnlyList<IndexedSliceElement>> LoadPageElementsAsync(
        SqliteConnection db,
        SqliteTransaction transaction,
        long documentId,
        SemanticPointer resolvedPointer,
        bool isDocumentRoot,
        int maxElements,
        CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select
                e.pointer,
                e.kind,
                e.text,
                e.heading_path,
                e.self_hash,
                e.subtree_hash,
                e.source_start,
                e.source_length
            from elements e
            where e.document_id = $documentId
              and (
                    ($isDocumentRoot = 1 and e.pointer <> 'document')
                    or
                    ($isDocumentRoot = 0 and e.ordinal >= (
                        select e2.ordinal
                        from elements e2
                        where e2.document_id = $documentId and e2.pointer = $ptr
                    ))
                  )
            order by e.ordinal
            limit $max
            """;
        command.AddParameter("$documentId", documentId);
        command.AddParameter("$ptr", resolvedPointer.Value);
        command.AddParameter("$isDocumentRoot", isDocumentRoot ? 1 : 0);
        command.AddParameter("$max", maxElements);

        var result = new List<IndexedSliceElement>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new IndexedSliceElement(
                reader.GetString(0),
                Enum.Parse<MarkdownElementKind>(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<SemanticAnchorCandidate>> LoadCandidatesAsync(
        SqliteConnection db,
        SqliteTransaction transaction,
        long documentId,
        CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select pointer, kind, text, self_hash, subtree_hash
            from elements
            where document_id = $documentId
              and source_length > 0
            order by ordinal
            """;
        command.AddParameter("$documentId", documentId);

        var result = new List<SemanticAnchorCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SemanticAnchorCandidate(
                new SemanticPointer(reader.GetString(0)),
                Enum.Parse<MarkdownElementKind>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return result;
    }

    private sealed record IndexedSliceElement(
        string LogicalPointer,
        MarkdownElementKind Kind,
        string Text,
        string? HeadingPath,
        string SelfHash,
        string SubtreeHash,
        int SourceStart,
        int SourceLength)
    {
        public string ToPublicPointer(bool includeFingerprints)
            => includeFingerprints
                ? new SemanticAnchor(
                    new SemanticPointer(LogicalPointer),
                    SelfHash,
                    SubtreeHash).ToString()
                : LogicalPointer;

        public MarkdownSliceElement ToSliceElement(bool includeFingerprints)
            => new(
                ToPublicPointer(includeFingerprints),
                Kind,
                Text,
                HeadingPath);
    }
}
