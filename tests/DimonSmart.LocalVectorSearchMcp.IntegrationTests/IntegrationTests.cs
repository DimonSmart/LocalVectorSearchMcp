using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Infrastructure;
namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task Indexer_IndexesSkipsReadsAndSearchesLexically()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nLocal vector search uses SQLite FTS5.\n");
        var config = TestConfig(temp.Path);
        var repository = new SqliteKnowledgeRepository(new SqliteConnectionFactory(config), config);
        var indexer = new KnowledgeBaseIndexer(config, new MarkdownDocumentLoader(), new MarkdownElementParser(), new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()), new FakeEmbeddingProvider(), repository);

        var first = await indexer.ReindexAsync(new ReindexRequest(null, ReindexScope.Changed, false), CancellationToken.None);
        var second = await indexer.ReindexAsync(new ReindexRequest(null, ReindexScope.Changed, false), CancellationToken.None);
        var lexical = await repository.SearchAsync("SQLite", 10, "kb", CancellationToken.None);
        var slice = await repository.ReadSliceAsync("kb", "notes.md", new SemanticPointer("1.p1"), 10, 12000, CancellationToken.None);

        Assert.Equal(1, first.IndexedFiles);
        Assert.Equal(1, second.SkippedFiles);
        Assert.NotEmpty(lexical);
        Assert.Contains("SQLite FTS5", slice.Markdown);
    }

    [Fact]
    public async Task SearchService_HybridCombinesLexicalAndSemantic()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nhybrid semantic lexical retrieval\n");
        var config = TestConfig(temp.Path);
        var repository = new SqliteKnowledgeRepository(new SqliteConnectionFactory(config), config);
        var provider = new FakeEmbeddingProvider();
        var indexer = new KnowledgeBaseIndexer(config, new MarkdownDocumentLoader(), new MarkdownElementParser(), new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()), provider, repository);
        await indexer.ReindexAsync(new ReindexRequest(null, ReindexScope.Changed, false), CancellationToken.None);

        var search = new KnowledgeSearchService(config, provider, repository, repository, repository);
        var response = await search.SearchAsync(new SearchRequest("hybrid", "kb", SearchMode.Hybrid, 5), CancellationToken.None);

        Assert.Single(response.Results);
        Assert.Equal("notes.md::1.p1", response.Results[0].FullPointer);
    }

    private static LocalVectorSearchMcpConfig TestConfig(string root) => new()
    {
        Storage = new StorageConfig { Path = Path.Combine(root, ".local-vector-search-mcp", "index.db") },
        Embedding = new EmbeddingConfig { Dimensions = 3 },
        KnowledgeBases = [new KnowledgeBaseConfig { Name = "kb", Root = root }]
    };
}
