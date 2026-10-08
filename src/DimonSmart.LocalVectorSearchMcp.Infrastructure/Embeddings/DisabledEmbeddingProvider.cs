using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Embeddings;

public sealed class DisabledEmbeddingProvider : IEmbeddingProvider
{
    public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
        => throw new EmbeddingProviderException(
            "Semantic search is disabled (embedding.provider: none).");
}
