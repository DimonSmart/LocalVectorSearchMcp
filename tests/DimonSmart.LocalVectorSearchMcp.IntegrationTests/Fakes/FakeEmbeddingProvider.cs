using DimonSmart.LocalVectorSearchMcp.Core;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

internal sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmbeddingVector> vectors = texts.Select(text =>
        {
            var seed = text.Contains("hybrid", StringComparison.OrdinalIgnoreCase) ? 1f : 0.5f;
            return new EmbeddingVector([seed, 0.2f, 0.1f]);
        }).ToList();
        return Task.FromResult(vectors);
    }
}
