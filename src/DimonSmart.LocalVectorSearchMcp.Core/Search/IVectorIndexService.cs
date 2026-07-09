using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;

namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public interface IVectorIndexService
{
    Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(EmbeddingVector queryEmbedding, int topK, CancellationToken cancellationToken);
}
