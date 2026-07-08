using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.SemanticPointers;

public sealed class SemanticPointerReader(KnowledgeBasePathGuard pathGuard, IKnowledgeRepository repository) : ISemanticPointerReader
{
    public Task<MarkdownSlice> ReadAsync(string? knowledgeBase, string path, SemanticPointer pointer, int maxElements, int maxBytes, CancellationToken cancellationToken)
    {
        var normalizedPath = pathGuard.ValidateRelativePath(knowledgeBase, path);
        return repository.ReadSliceAsync(knowledgeBase, normalizedPath, pointer, Math.Clamp(maxElements, 1, 100), Math.Clamp(maxBytes, 1024, 50000), cancellationToken);
    }
}
