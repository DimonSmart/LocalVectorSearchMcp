using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class LexicalOnlyIntegrationTests
{
    [Fact]
    public async Task Lexical_only_indexes_and_searches_without_a_vector_provider_or_table()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Intent\nlexical-marker design decision\n", ct);
        var config = LexicalConfig(temp.Path);
        var services = SqliteTestServices.Create(config);
        var provider = new FailIfCalledEmbeddingProvider();
        var indexer = CreateIndexer(config, services, provider);

        var first = await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);
        var second = await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);
        var search = new KnowledgeSearchService(config, provider, services.VectorSearch,
            services.FullTextSearch, services.SearchIndexReader, services.SearchIndexReader);
        var lexical = await search.SearchAsync(new SearchRequest("lexical-marker", SearchMode.Lexical, 5), ct);
        var hybrid = await search.SearchAsync(new SearchRequest("lexical-marker", SearchMode.Hybrid, 5), ct);
        var semanticError = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => search.SearchAsync(new SearchRequest("lexical-marker", SearchMode.Semantic, 5), ct));

        Assert.Equal(1, first.IndexedFiles);
        Assert.Equal(1, second.SkippedFiles);
        Assert.Single(lexical.Results);
        Assert.Null(lexical.Warning);
        Assert.Equal(SearchMode.Lexical, Assert.Single(hybrid.Results).SearchMode);
        Assert.Contains("lexical", hybrid.Warning);
        Assert.Contains("disabled", semanticError.Message);
        Assert.Equal(0, provider.Calls);

        await using var db = new SqliteConnectionFactory(config).Open();
        var command = db.CreateCommand();
        command.CommandText = "select count(*) from sqlite_master where type = 'table' and name = 'chunk_vectors'";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(ct))!);

        var status = await new SqliteIndexStatusReader(new SqliteConnectionFactory(config), config,
            manifestService: services.Manifest).GetStatusAsync(ct);
        Assert.Equal("lexical", status.IndexMode);
        Assert.Equal("none", status.EmbeddingProvider);
        Assert.Equal("none", status.EmbeddingModel);
        Assert.Null(status.EmbeddingDimensions);
        Assert.True(status.Compatibility?.IsCompatible);
        Assert.Equal(1, status.Project.Documents);
    }

    [Fact]
    public async Task Lexical_only_reconciliation_updates_and_removes_a_single_document()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "notes.md");
        await File.WriteAllTextAsync(path, "# Original\nfirst-marker\n", ct);
        var config = LexicalConfig(temp.Path);
        var services = SqliteTestServices.Create(config);
        var provider = new FailIfCalledEmbeddingProvider();
        await CreateIndexer(config, services, provider).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);
        var reconciler = new WorkspaceIndexSynchronizer(config, new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(), new MarkdownElementParser(),
            new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()), provider,
            services.Initializer, services.Manifest, services.DocumentStore);

        await File.WriteAllTextAsync(path, "# Changed\nsecond-marker\n", ct);
        Assert.True(await reconciler.ReconcileAsync("notes.md", ct));
        Assert.Empty(await services.FullTextSearch.SearchAsync("first-marker", 10, ct));
        Assert.NotEmpty(await services.FullTextSearch.SearchAsync("second-marker", 10, ct));

        File.Delete(path);
        Assert.True(await reconciler.ReconcileAsync("notes.md", ct));
        Assert.Empty(await services.DocumentStore.GetDocumentHashesAsync(ct));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Index_mode_change_requires_force_and_rebuilds_without_touching_markdown()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "notes.md");
        await File.WriteAllTextAsync(file, "# Intent\nmode-switch-marker\n", ct);
        var config = LexicalConfig(temp.Path);
        var services = SqliteTestServices.Create(config);
        await CreateIndexer(config, services, new FailIfCalledEmbeddingProvider())
            .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);

        var vectorConfig = config with { Embedding = new EmbeddingConfig { Dimensions = 3 } };
        var vectorServices = SqliteTestServices.Create(vectorConfig);
        await vectorServices.Initializer.InitializeAsync(ct);
        var compatibility = await vectorServices.Manifest.CheckCompatibilityAsync(ct);
        Assert.False(compatibility.IsCompatible);
        Assert.Contains(compatibility.Problems, p => p.Contains("index_mode", StringComparison.Ordinal));
        Assert.NotEmpty(await services.FullTextSearch.SearchAsync("mode-switch-marker", 10, ct));
    }


    [Fact]
    public async Task Switching_from_vector_index_to_lexical_requires_force()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "notes.md");
        await File.WriteAllTextAsync(path, "# Intent\noriginal-mode-marker\n", ct);

        var lexical = LexicalConfig(temp.Path);
        var vectorConfig = lexical with { Embedding = new EmbeddingConfig { Dimensions = 3 } };
        var vectorServices = SqliteTestServices.Create(vectorConfig);
        var vectorIndexer = CreateIndexer(vectorConfig, vectorServices,
            new Fakes.FakeEmbeddingProvider(3));
        await vectorIndexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);

        var lexicalServices = SqliteTestServices.Create(lexical);
        var lexicalIndexer = CreateIndexer(lexical, lexicalServices, new FailIfCalledEmbeddingProvider());
        await Assert.ThrowsAsync<IndexCompatibilityException>(
            () => lexicalIndexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct));
        Assert.True(File.Exists(path));

        var rebuilt = await lexicalIndexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, true), ct);
        Assert.Equal(1, rebuilt.IndexedFiles);
        Assert.Contains("original-mode-marker", await File.ReadAllTextAsync(path, ct));
        Assert.NotEmpty(await lexicalServices.FullTextSearch.SearchAsync("original-mode-marker", 10, ct));

        await using var db = new SqliteConnectionFactory(lexical).Open();
        var command = db.CreateCommand();
        command.CommandText = "select count(*) from sqlite_master where name = 'chunk_vectors'";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(ct))!);
    }

    [Fact]
    public async Task Explicit_project_roots_keep_index_data_separate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var rootA = new TemporaryDirectory();
        using var rootB = new TemporaryDirectory();
        using var otherCwd = new TemporaryDirectory();
        foreach (var root in new[] { rootA.Path, rootB.Path })
        {
            Directory.CreateDirectory(Path.Combine(root, ".idd", "intent"));
        }

        await File.WriteAllTextAsync(Path.Combine(rootA.Path, ".idd", "intent", "shared.md"),
            "# A\nproject-alpha-token\n", ct);
        await File.WriteAllTextAsync(Path.Combine(rootB.Path, ".idd", "intent", "shared.md"),
            "# B\nproject-beta-token\n", ct);

        async Task<(LocalVectorSearchMcpConfig Config, SqliteTestServices Services)> Start(string project)
        {
            var loaded = Infrastructure.Configuration.LocalVectorSearchConfigLoader.Load(
                ["--project-root", project, "--root", ".idd/intent",
                    "--embedding-provider", "none", "--search-mode", "lexical"], otherCwd.Path, "");
            var services = SqliteTestServices.Create(loaded);
            await CreateIndexer(loaded, services, new FailIfCalledEmbeddingProvider())
                .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);
            return (loaded, services);
        }

        var first = await Start(rootA.Path);
        var second = await Start(rootB.Path);
        Assert.NotEqual(first.Config.Storage.Path, second.Config.Storage.Path);
        Assert.NotEmpty(await first.Services.FullTextSearch.SearchAsync("project-alpha-token", 10, ct));
        Assert.Empty(await first.Services.FullTextSearch.SearchAsync("project-beta-token", 10, ct));
        Assert.NotEmpty(await second.Services.FullTextSearch.SearchAsync("project-beta-token", 10, ct));
        Assert.Empty(await second.Services.FullTextSearch.SearchAsync("project-alpha-token", 10, ct));
    }

    private static LocalVectorSearchMcpConfig LexicalConfig(string root) => new()
    {
        Storage = new StorageConfig { Path = Path.Combine(root, "index.db") },
        KnowledgeBase = new KnowledgeBaseConfig { Root = root },
        Embedding = new EmbeddingConfig { Provider = "none", Model = "", ApiKey = "", Endpoint = "" },
        Search = new SearchConfig { DefaultMode = SearchMode.Lexical }
    };

    private static KnowledgeBaseIndexer CreateIndexer(
        LocalVectorSearchMcpConfig config,
        SqliteTestServices services,
        IEmbeddingProvider provider)
        => new(config, new MarkdownDocumentLoader(), new MarkdownElementParser(),
            new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()), provider,
            services.Initializer, services.DocumentStore, services.Manifest);

    private sealed class FailIfCalledEmbeddingProvider : IEmbeddingProvider
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Embedding provider must not be called.");
        }
    }
}
