using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class KnowledgeSearchFallbackTests
{
    [Fact]
    public async Task HybridSearch_FallsBackToLexicalWhenEmbeddingsAreUnavailable()
    {
        var service = CreateService();

        var response = await service.SearchAsync(
            new SearchRequest("needle", SearchMode.Hybrid, 5),
            TestContext.Current.CancellationToken);

        var result = Assert.Single(response.Results);
        Assert.Equal(SearchMode.Lexical, result.SearchMode);
        Assert.Equal("docs/notes.md", result.Path);
        Assert.Contains(
            "Falling back to lexical search",
            response.Warning,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticSearch_DoesNotSilentlyChangeSearchMode()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(() =>
            service.SearchAsync(
                new SearchRequest("needle", SearchMode.Semantic, 5),
                TestContext.Current.CancellationToken));

        Assert.Equal("embedding unavailable", exception.Message);
    }

    private static KnowledgeSearchService CreateService()
        => new(
            new LocalVectorSearchMcpConfig(),
            new FailingEmbeddingProvider(),
            new UnexpectedVectorSearch(),
            new FakeFullTextSearch(),
            new ReadyIndexState(),
            new FakeChunkReader());

    private sealed class FailingEmbeddingProvider : IEmbeddingProvider
    {
        public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
            => throw new EmbeddingProviderException("embedding unavailable");
    }

    private sealed class UnexpectedVectorSearch : IVectorIndexService
    {
        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
            EmbeddingVector queryEmbedding,
            int topK,
            SearchPathScope? scope,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Vector search must not run without an embedding.");
    }

    private sealed class FakeFullTextSearch : IFullTextSearchService
    {
        public Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(
            string query,
            int topK,
            SearchPathScope? scope,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LexicalSearchResult>>(
                [new LexicalSearchResult(1, 1, "needle body")]);
    }

    private sealed class ReadyIndexState : ISearchIndexStateReader
    {
        public Task<bool> HasChunksAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class FakeChunkReader : IChunkSearchDocumentReader
    {
        public Task<IReadOnlyList<ChunkSearchDocument>> GetChunksAsync(
            IReadOnlyCollection<long> chunkIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChunkSearchDocument>>(
                [new ChunkSearchDocument(
                    1,
                    "docs/notes.md",
                    "1.p1",
                    "needle body",
                    "Notes",
                    "needle body",
                    "0123456789abcdef",
                    "0123456789abcdef")]);
    }
}
