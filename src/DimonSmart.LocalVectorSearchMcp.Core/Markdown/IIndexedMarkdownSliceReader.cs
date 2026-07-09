using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public interface IIndexedMarkdownSliceReader
{
    Task<MarkdownSlice> ReadSliceAsync(
        string path,
        SemanticPointer pointer,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken);
}
