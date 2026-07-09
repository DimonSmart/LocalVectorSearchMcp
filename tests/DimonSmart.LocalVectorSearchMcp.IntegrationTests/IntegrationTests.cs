using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Fakes;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task Indexer_IndexesSkipsReadsAndSearchesLexically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nLocal vector search uses SQLite FTS5.\n", cancellationToken);
        var config = TestConfig(temp.Path);
        var repository = SqliteTestServices.Create(config);
        var indexer = CreateIndexer(config, repository);

        var first = await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        var second = await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        var lexical = await repository.FullTextSearch.SearchAsync("SQLite", 10, cancellationToken);
        var slice = await repository.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer("1.p1"), 10, 12000, cancellationToken);

        Assert.Equal(1, first.IndexedFiles);
        Assert.Equal(1, second.SkippedFiles);
        Assert.NotEmpty(lexical);
        Assert.Contains("SQLite FTS5", slice.Markdown);
        Assert.Equal("2", await ReadManifestValueAsync(config, "schema_version", cancellationToken));
        Assert.DoesNotContain("knowledge_base", await ReadColumnNamesAsync(config, "documents", cancellationToken));
        Assert.DoesNotContain("knowledge_base", await ReadColumnNamesAsync(config, "chunks", cancellationToken));
    }

    [Fact]
    public async Task SearchService_HybridCombinesLexicalAndSemantic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nhybrid semantic lexical retrieval\n", cancellationToken);
        var config = TestConfig(temp.Path);
        var repository = SqliteTestServices.Create(config);
        var provider = new FakeEmbeddingProvider();
        var indexer = CreateIndexer(config, repository, provider);
        await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var search = new KnowledgeSearchService(config, provider, repository.VectorSearch, repository.FullTextSearch, repository.SearchIndexReader, repository.SearchIndexReader);
        var response = await search.SearchAsync(new SearchRequest("hybrid", SearchMode.Hybrid, 5), cancellationToken);

        Assert.Single(response.Results);
        Assert.Equal("notes.md::1.p1", response.Results[0].FullPointer);
    }

    [Fact]
    public async Task ForceFalse_FailsOnIncompatibleManifest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nText.\n", cancellationToken);
        var configA = TestConfig(temp.Path, "model-a", 3);
        var repositoryA = SqliteTestServices.Create(configA);
        await CreateIndexer(configA, repositoryA).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var configB = TestConfig(temp.Path, "model-b", 3);
        var repositoryB = SqliteTestServices.Create(configB);
        var exception = await Assert.ThrowsAsync<IndexCompatibilityException>(
            () => CreateIndexer(configB, repositoryB).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken));

        Assert.Contains("embedding_model", exception.Message);
        Assert.Contains("model-a", exception.Message);
        Assert.Contains("model-b", exception.Message);
        Assert.Contains("force=true", exception.Message);
    }

    [Fact]
    public async Task ForceTrue_RebuildsIncompatibleIndexAndManifest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nText.\n", cancellationToken);
        var configA = TestConfig(temp.Path, "model-a", 3);
        var repositoryA = SqliteTestServices.Create(configA);
        await CreateIndexer(configA, repositoryA).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var configB = TestConfig(temp.Path, "model-b", 3);
        var repositoryB = SqliteTestServices.Create(configB);
        var response = await CreateIndexer(configB, repositoryB).ReindexAsync(new ReindexRequest(ReindexScope.Changed, true), cancellationToken);
        var status = await repositoryB.StatusReader.GetStatusAsync(cancellationToken);

        Assert.Equal(1, response.IndexedFiles);
        Assert.Equal("model-b", status.EmbeddingModel);
        Assert.Equal("model-b", await ReadManifestValueAsync(configB, "embedding_model", cancellationToken));
    }

    [Fact]
    public async Task DimensionsChange_RequiresForceAndRecreatesVectorTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nText.\n", cancellationToken);
        var configA = TestConfig(temp.Path, "model", 3);
        var repositoryA = SqliteTestServices.Create(configA);
        await CreateIndexer(configA, repositoryA).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var configB = TestConfig(temp.Path, "model", 4);
        var repositoryB = SqliteTestServices.Create(configB);
        var indexerB = CreateIndexer(configB, repositoryB, new FakeEmbeddingProvider(4));
        var exception = await Assert.ThrowsAsync<IndexCompatibilityException>(
            () => indexerB.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken));
        var response = await indexerB.ReindexAsync(new ReindexRequest(ReindexScope.Changed, true), cancellationToken);

        Assert.Contains("embedding_dimensions", exception.Message);
        Assert.Equal(1, response.IndexedFiles);
        Assert.Equal("4", await ReadManifestValueAsync(configB, "embedding_dimensions", cancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_DoesNotHideManifestMismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nText.\n", cancellationToken);
        var configA = TestConfig(temp.Path, "model-a", 3);
        var repositoryA = SqliteTestServices.Create(configA);
        await CreateIndexer(configA, repositoryA).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var configB = TestConfig(temp.Path, "model-b", 3);
        var repositoryB = SqliteTestServices.Create(configB);
        await repositoryB.Initializer.InitializeAsync(cancellationToken);
        var compatibility = await repositoryB.Manifest.CheckCompatibilityAsync(cancellationToken);

        Assert.Equal("model-a", await ReadManifestValueAsync(configB, "embedding_model", cancellationToken));
        Assert.False(compatibility.IsCompatible);
        Assert.Contains(compatibility.Problems, problem => problem.Contains("embedding_model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmbeddingDimensionMismatch_FailsBeforeDocumentStorage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Alpha\nText.\n", cancellationToken);
        var config = TestConfig(temp.Path, "model", 3);
        var repository = SqliteTestServices.Create(config);

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => CreateIndexer(config, repository, new FakeEmbeddingProvider(4))
                .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken));
        var status = await repository.StatusReader.GetStatusAsync(cancellationToken);

        Assert.Contains("Expected 3, got 4", exception.Message);
        Assert.Equal(0, status.Project.Documents);
    }

    [Fact]
    public async Task ChangedReindex_RemovesDocumentsExcludedByUpdatedConfiguration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "docs", "private"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "docs", "a.md"), "# Public\npublic-marker\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "docs", "private", "secret.md"), "# Secret\nsecret-marker\n", cancellationToken);

        var initialConfig = TestConfig(temp.Path) with
        {
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = temp.Path,
                Include = ["docs/**/*.md"],
                Exclude = []
            }
        };
        var initialRepository = SqliteTestServices.Create(initialConfig);
        await CreateIndexer(initialConfig, initialRepository)
            .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        Assert.Equal(2, (await initialRepository.StatusReader.GetStatusAsync(cancellationToken)).Project.Documents);

        var updatedConfig = initialConfig with
        {
            KnowledgeBase = initialConfig.KnowledgeBase with { Exclude = ["docs/private/**"] }
        };
        var updatedRepository = SqliteTestServices.Create(updatedConfig);
        var response = await CreateIndexer(updatedConfig, updatedRepository)
            .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        var hashes = await updatedRepository.DocumentStore.GetDocumentHashesAsync(cancellationToken);

        Assert.Equal(1, response.DeletedFiles);
        Assert.Equal(["docs/a.md"], hashes.Keys);
        Assert.Equal(1, (await updatedRepository.StatusReader.GetStatusAsync(cancellationToken)).Project.Documents);
        Assert.Empty(await updatedRepository.FullTextSearch.SearchAsync("secret-marker", 10, cancellationToken));
        Assert.NotEmpty(await updatedRepository.FullTextSearch.SearchAsync("public-marker", 10, cancellationToken));

        var counts = await ReadIndexRowCountsAsync(updatedConfig, cancellationToken);
        Assert.Equal(1, counts.Documents);
        Assert.True(counts.Elements > 0);
        Assert.True(counts.Chunks > 0);
        Assert.Equal(counts.Chunks, counts.FtsRows);
        Assert.Equal(counts.Chunks, counts.VectorRows);
    }

    [Fact]
    public async Task ChangedReindex_RemovesPhysicallyDeletedDocumentAndRelatedRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var docs = Path.Combine(temp.Path, "docs");
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(docs, "a.md"), "# A\nremaining-marker\n", cancellationToken);
        var deletedPath = Path.Combine(docs, "b.md");
        await File.WriteAllTextAsync(deletedPath, "# B\ndeleted-marker\n", cancellationToken);

        var config = TestConfig(temp.Path) with
        {
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = temp.Path,
                Include = ["docs/**/*.md"],
                Exclude = []
            }
        };
        var repository = SqliteTestServices.Create(config);
        var indexer = CreateIndexer(config, repository);
        await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        File.Delete(deletedPath);
        var response = await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        var paths = (await repository.DocumentStore.GetDocumentHashesAsync(cancellationToken)).Keys;
        var counts = await ReadIndexRowCountsAsync(config, cancellationToken);

        Assert.Equal(1, response.DeletedFiles);
        Assert.Equal(["docs/a.md"], paths);
        Assert.Equal(1, counts.Documents);
        Assert.True(counts.Elements > 0);
        Assert.True(counts.Chunks > 0);
        Assert.Equal(counts.Chunks, counts.FtsRows);
        Assert.Equal(counts.Chunks, counts.VectorRows);
        Assert.Empty(await repository.FullTextSearch.SearchAsync("deleted-marker", 10, cancellationToken));
    }

    [Fact]
    public async Task DeleteMissingDocuments_RemovesAllRelatedRowsByCascade()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "notes.md"),
            "# Notes\ncascade-marker\n",
            cancellationToken);
        var config = TestConfig(temp.Path);
        var repository = SqliteTestServices.Create(config);
        await CreateIndexer(config, repository)
            .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var before = await ReadIndexRowCountsAsync(config, cancellationToken);
        Assert.True(before.Documents > 0);
        Assert.True(before.Elements > 0);
        Assert.True(before.Chunks > 0);
        Assert.True(before.FtsRows > 0);
        Assert.True(before.VectorRows > 0);

        var deleted = await repository.DocumentStore.DeleteMissingDocumentsAsync(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        var after = await ReadIndexRowCountsAsync(config, cancellationToken);

        Assert.Equal(1, deleted);
        Assert.Equal((0L, 0L, 0L, 0L, 0L), after);
    }

    private static KnowledgeBaseIndexer CreateIndexer(
        LocalVectorSearchMcpConfig config,
        SqliteTestServices repository,
        IEmbeddingProvider? provider = null)
        => new(config, new MarkdownDocumentLoader(), new MarkdownElementParser(), new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()), provider ?? new FakeEmbeddingProvider(config.Embedding.Dimensions ?? 3), repository.Initializer, repository.DocumentStore, repository.Manifest);

    private static async Task<string?> ReadManifestValueAsync(LocalVectorSearchMcpConfig config, string key, CancellationToken cancellationToken)
    {
        await using var db = new SqliteConnectionFactory(config).Open();
        var command = db.CreateCommand();
        command.CommandText = "select value from index_manifest where key = $key";
        command.Parameters.AddWithValue("$key", key);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<string>> ReadColumnNamesAsync(LocalVectorSearchMcpConfig config, string table, CancellationToken cancellationToken)
    {
        await using var db = new SqliteConnectionFactory(config).Open();
        var command = db.CreateCommand();
        command.CommandText = $"pragma table_info({table})";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(1));
        return names;
    }

    private static async Task<(long Documents, long Elements, long Chunks, long FtsRows, long VectorRows)> ReadIndexRowCountsAsync(
        LocalVectorSearchMcpConfig config,
        CancellationToken cancellationToken)
    {
        await using var db = new SqliteConnectionFactory(config).Open();
        SqliteVectorExtensionLoader.Load(db);

        static async Task<long> CountAsync(Microsoft.Data.Sqlite.SqliteConnection db, string table, CancellationToken cancellationToken)
        {
            var command = db.CreateCommand();
            command.CommandText = $"select count(*) from {table}";
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        return (
            await CountAsync(db, "documents", cancellationToken),
            await CountAsync(db, "elements", cancellationToken),
            await CountAsync(db, "chunks", cancellationToken),
            await CountAsync(db, "chunks_fts", cancellationToken),
            await CountAsync(db, "chunk_vectors", cancellationToken));
    }

    private static LocalVectorSearchMcpConfig TestConfig(string root, string model = "bge-m3:latest", int dimensions = 3) => new()
    {
        Storage = new StorageConfig { Path = Path.Combine(root, ".local-vector-search-mcp", "index.db") },
        Embedding = new EmbeddingConfig { Model = model, Dimensions = dimensions },
        KnowledgeBase = new KnowledgeBaseConfig { Root = root }
    };
}
