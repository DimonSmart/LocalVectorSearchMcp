namespace DimonSmart.LocalVectorSearchMcp.Core.Embeddings;

public interface IEmbeddingProvider
{
    Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
