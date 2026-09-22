using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public interface IMarkdownSliceReader
{
    Task<MarkdownSlice> ReadSliceAsync(
        string path,
        SemanticPointer pointer,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken);

    Task<MarkdownSlice> ReadSliceAsync(
        string path,
        SemanticAnchor anchor,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken);
}
