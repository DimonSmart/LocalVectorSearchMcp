namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IVectorIndexService
{
    Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(EmbeddingVector queryEmbedding, int topK, string? knowledgeBase, CancellationToken cancellationToken);
}
