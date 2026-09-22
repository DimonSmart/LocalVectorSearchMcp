using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

public sealed class SourceMarkdownSliceReader(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard,
    IMarkdownDocumentLoader loader,
    IMarkdownElementParser parser) : IMarkdownSliceReader
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

        var normalized = pathGuard.ValidateRelativePath(path);
        _ = pathGuard.ResolveMarkdownPath(normalized);
        var document = await loader.LoadExistingAsync(
            config.KnowledgeBase,
            normalized,
            cancellationToken);

        var parsedElements = parser.Parse(document)
            .Where(element =>
                element.Kind != MarkdownElementKind.Document
                && element.SourceLength > 0)
            .ToList();

        var resolvedPointer = anchor.LogicalPointer;
        if (includeFingerprints && anchor.Fingerprint is not null)
        {
            var candidates = parsedElements
                .Select(element => new SemanticAnchorCandidate(
                    element.Pointer,
                    element.Kind,
                    element.Text,
                    element.SelfHash,
                    element.SubtreeHash))
                .ToList();
            resolvedPointer = SemanticAnchorResolver.Resolve(anchor, candidates);
        }

        var isDocumentRoot =
            SemanticPointerParser.GetKind(resolvedPointer) == SemanticPointerKind.Document;
        IReadOnlyList<MarkdownElement> pageElements;
        if (isDocumentRoot)
        {
            pageElements = parsedElements.Take(maxElements + 1).ToList();
        }
        else
        {
            var startIndex = parsedElements.FindIndex(element =>
                element.Pointer.Value.Equals(
                    resolvedPointer.Value,
                    StringComparison.Ordinal));
            if (startIndex < 0)
            {
                throw PointerNotFound(normalized, anchor);
            }

            pageElements = parsedElements
                .Skip(startIndex)
                .Take(maxElements + 1)
                .ToList();
        }

        if (pageElements.Count == 0)
        {
            if (!isDocumentRoot)
            {
                throw PointerNotFound(normalized, anchor);
            }

            return new MarkdownSlice(
                normalized,
                "document",
                [],
                document.Markdown,
                null,
                document.SourceHash);
        }

        var includedCount = SelectElementCount(
            document.Markdown,
            pageElements,
            isDocumentRoot,
            maxElements,
            maxBytes);
        var includedElements = pageElements
            .Take(includedCount)
            .Select(element => ToSliceElement(element, includeFingerprints))
            .ToList();
        var nextElement = includedCount < pageElements.Count
            ? pageElements[includedCount]
            : null;

        var sourceStart = isDocumentRoot
            ? 0
            : GetReadBoundaryStart(document.Markdown, pageElements[0].SourceStart);
        var sourceEnd = nextElement is null
            ? document.Markdown.Length
            : GetReadBoundaryStart(document.Markdown, nextElement.SourceStart);
        sourceEnd = Math.Clamp(sourceEnd, sourceStart, document.Markdown.Length);

        var responsePointer = isDocumentRoot
            ? "document"
            : includedElements[0].Pointer;
        var nextPointer = nextElement is null
            ? null
            : ToPublicPointer(nextElement, includeFingerprints);
        var markdown = document.Markdown.Substring(
            sourceStart,
            sourceEnd - sourceStart);

        return new MarkdownSlice(
            normalized,
            responsePointer,
            includedElements,
            markdown,
            nextPointer,
            document.SourceHash);
    }

    private static int SelectElementCount(
        string sourceMarkdown,
        IReadOnlyList<MarkdownElement> elements,
        bool isDocumentRoot,
        int maxElements,
        int maxBytes)
    {
        var sourceStart = isDocumentRoot
            ? 0
            : GetReadBoundaryStart(sourceMarkdown, elements[0].SourceStart);
        var candidateCount = Math.Min(maxElements, elements.Count);
        var includedCount = 0;

        for (var index = 0; index < candidateCount; index++)
        {
            var sourceEnd = index + 1 < elements.Count
                ? GetReadBoundaryStart(
                    sourceMarkdown,
                    elements[index + 1].SourceStart)
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

    private static string ToPublicPointer(
        MarkdownElement element,
        bool includeFingerprints)
        => includeFingerprints
            ? SemanticAnchor.FromElement(element).ToString()
            : element.Pointer.Value;

    private static MarkdownSliceElement ToSliceElement(
        MarkdownElement element,
        bool includeFingerprints)
        => new(
            ToPublicPointer(element, includeFingerprints),
            element.Kind,
            element.Text,
            element.HeadingPath);

    private static SemanticPointerNotFoundException PointerNotFound(
        string path,
        SemanticAnchor anchor)
        => new(
            $"Pointer '{anchor.LogicalPointer.Value}' was not found in '{path}'.");
}
