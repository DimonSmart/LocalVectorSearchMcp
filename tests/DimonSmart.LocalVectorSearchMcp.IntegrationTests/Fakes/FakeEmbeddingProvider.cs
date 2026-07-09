using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests.Fakes;

internal sealed class FakeEmbeddingProvider(int dimensions = 3) : IEmbeddingProvider
{
    public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmbeddingVector> vectors = texts.Select(text =>
        {
            var seed = text.Contains("hybrid", StringComparison.OrdinalIgnoreCase) ? 1f : 0.5f;
            var values = new float[dimensions];
            values[0] = seed;
            if (dimensions > 1) values[1] = 0.2f;
            if (dimensions > 2) values[2] = 0.1f;
            return new EmbeddingVector(values);
        }).ToList();
        return Task.FromResult(vectors);
    }
}
