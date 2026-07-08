using DimonSmart.LocalVectorSearchMcp.Core;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure;

public sealed class SemanticPointerReader(KnowledgeBasePathGuard pathGuard, IKnowledgeRepository repository) : ISemanticPointerReader
{
    public Task<MarkdownSlice> ReadAsync(string? knowledgeBase, string path, SemanticPointer pointer, int maxElements, int maxBytes, CancellationToken cancellationToken)
    {
        var normalizedPath = pathGuard.ValidateRelativePath(knowledgeBase, path);
        return repository.ReadSliceAsync(knowledgeBase, normalizedPath, pointer, Math.Clamp(maxElements, 1, 100), Math.Clamp(maxBytes, 1024, 50000), cancellationToken);
    }
}
