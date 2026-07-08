namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IEmbeddingProvider
{
    Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
