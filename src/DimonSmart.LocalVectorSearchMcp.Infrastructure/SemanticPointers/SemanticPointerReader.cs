using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.SemanticPointers;

public sealed class SemanticPointerReader(KnowledgeBasePathGuard pathGuard, IIndexedMarkdownSliceReader sliceReader) : ISemanticPointerReader
{
    public Task<MarkdownSlice> ReadAsync(string path, SemanticPointer pointer, int maxElements, int maxBytes, CancellationToken cancellationToken)
    {
        var normalizedPath = pathGuard.ValidateRelativePath(path);
        return sliceReader.ReadSliceAsync(normalizedPath, pointer, Math.Clamp(maxElements, 1, 100), Math.Clamp(maxBytes, 256, 100_000), cancellationToken);
    }
}
