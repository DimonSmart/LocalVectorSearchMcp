using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.SemanticPointers;

public sealed class SemanticPointerReader(
    KnowledgeBasePathGuard pathGuard,
    IMarkdownSliceReader sliceReader) : ISemanticPointerReader
{
    public Task<MarkdownSlice> ReadAsync(
        string path,
        SemanticAnchor anchor,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var normalizedPath = pathGuard.ValidateRelativePath(path);
        return sliceReader.ReadSliceAsync(
            normalizedPath,
            anchor,
            Math.Clamp(maxElements, 1, 100),
            Math.Clamp(maxBytes, 256, 100_000),
            cancellationToken);
    }
}
