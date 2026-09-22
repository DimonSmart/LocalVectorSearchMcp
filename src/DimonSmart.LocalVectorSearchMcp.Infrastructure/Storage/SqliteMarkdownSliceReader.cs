using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;

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

        var documentCommand = db.CreateCommand();
        documentCommand.CommandText = "select id, source_hash from documents where path = $path";
        documentCommand.AddParameter("$path", path);

        long documentId;
        string sourceHash;
        await using (var documentReader = await documentCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await documentReader.ReadAsync(cancellationToken))
            {
                throw new SemanticPointerNotFoundException(
                    $"Pointer '{anchor.LogicalPointer.Value}' was not found in '{path}'.");
            }

            documentId = documentReader.GetInt64(0);
            sourceHash = documentReader.GetString(1);
        }

        var resolvedPointer = anchor.LogicalPointer;
        if (includeFingerprints && anchor.Fingerprint is not null)
        {
            var candidates = await LoadCandidatesAsync(db, documentId, cancellationToken);
            resolvedPointer = SemanticAnchorResolver.Resolve(anchor, candidates);
        }

        var isDocumentRoot =
            SemanticPointerParser.GetKind(resolvedPointer) == SemanticPointerKind.Document;
        var command = db.CreateCommand();
        command.CommandText = """
            select e.pointer, e.kind, e.text, e.heading_path, e.self_hash, e.subtree_hash
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
        command.AddParameter("$max", maxElements + 1);

        var elements = new List<MarkdownSliceElement>();
        string? nextPointer = null;
        var bytes = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var logicalPointer = reader.GetString(0);
            var kind = Enum.Parse<MarkdownElementKind>(reader.GetString(1));
            var elementText = reader.GetString(2);
            var publicPointer = includeFingerprints
                ? new SemanticAnchor(
                    new SemanticPointer(logicalPointer),
                    reader.GetString(4),
                    reader.GetString(5)).ToString()
                : logicalPointer;

            if (elements.Count >= maxElements)
            {
                nextPointer = publicPointer;
                break;
            }

            var separatorBytes = elements.Count == 0 ? 0 : Encoding.UTF8.GetByteCount("\n\n");
            var projectedBytes = bytes + separatorBytes + Encoding.UTF8.GetByteCount(elementText);
            if (elements.Count > 0 && projectedBytes > maxBytes)
            {
                nextPointer = publicPointer;
                break;
            }

            elements.Add(new MarkdownSliceElement(
                publicPointer,
                kind,
                elementText,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
            bytes = projectedBytes;
        }

        if (elements.Count == 0 && !isDocumentRoot)
        {
            throw new SemanticPointerNotFoundException(
                $"Pointer '{anchor.LogicalPointer.Value}' was not found in '{path}'.");
        }

        var responsePointer = isDocumentRoot
            ? "document"
            : includeFingerprints
                ? elements[0].Pointer
                : resolvedPointer.Value;
        var markdown = string.Join("\n\n", elements.Select(element => element.Text));
        return new MarkdownSlice(
            path,
            responsePointer,
            elements,
            markdown,
            nextPointer,
            sourceHash);
    }

    private static async Task<IReadOnlyList<SemanticAnchorCandidate>> LoadCandidatesAsync(
        Microsoft.Data.Sqlite.SqliteConnection db,
        long documentId,
        CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
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
}
