using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Fakes;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ChunkingManifestCompatibilityTests
{
    [Fact]
    public Task ChangingIncludeHeadingContextRequiresForce()
        => VerifyChangeRequiresForce(
            config => config with { IncludeHeadingContext = false },
            "chunking_include_heading_context",
            "true",
            "false");

    [Fact]
    public Task ChangingIncludeFrontMatterRequiresForce()
        => VerifyChangeRequiresForce(
            config => config with { IncludeFrontMatter = false },
            "chunking_include_front_matter",
            "true",
            "false");

    [Fact]
    public Task ChangingMaxElementsRequiresForce()
        => VerifyChangeRequiresForce(
            config => config with { MaxElements = 10 },
            "chunking_max_elements",
            "20",
            "10");

    [Fact]
    public Task ChangingMaxChunkBytesRequiresForce()
        => VerifyChangeRequiresForce(
            config => config with { MaxChunkBytes = 2048 },
            "chunking_max_chunk_bytes",
            "4096",
            "2048");

    [Fact]
    public async Task CurrentManifestContainsChunkingSettings()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Heading\n\nText.", cancellationToken);
        var config = TestConfig(temp.Path);
        var repository = CreateRepository(config);

        await CreateIndexer(config, repository).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        var manifest = await ReadManifestAsync(config, cancellationToken);

        Assert.Equal("2", manifest["schema_version"]);
        Assert.Equal(MarkdownChunker.Version, manifest["chunker_version"]);
        Assert.Equal(EmbeddingTextBuilder.Version, manifest["embedding_text_builder_version"]);
        Assert.Equal("model", manifest["embedding_model"]);
        Assert.Equal("3", manifest["embedding_dimensions"]);
        Assert.Equal("4096", manifest["chunking_max_chunk_bytes"]);
        Assert.Equal("20", manifest["chunking_max_elements"]);
        Assert.Equal("true", manifest["chunking_include_heading_context"]);
        Assert.Equal("true", manifest["chunking_include_front_matter"]);
    }

    [Fact]
    public async Task MissingChunkingManifestKeyRequiresForce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Heading\n\nText.", cancellationToken);
        var config = TestConfig(temp.Path);
        var repository = CreateRepository(config);
        await CreateIndexer(config, repository).ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        await DeleteManifestKeyAsync(config, "chunking_include_heading_context", cancellationToken);

        var exception = await Assert.ThrowsAsync<IndexCompatibilityException>(
            () => CreateIndexer(config, CreateRepository(config))
                .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken));

        Assert.Contains("chunking_include_heading_context: indexed <missing>, current 'true'", exception.Message);
        Assert.Contains("force=true", exception.Message);
    }

    [Fact]
    public async Task ForceDoesNotRecreateCompatibleIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Heading\n\nText.", cancellationToken);
        var config = TestConfig(temp.Path);
        var repository = CreateRepository(config);
        var indexer = CreateIndexer(config, repository);
        await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var response = await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, true), cancellationToken);

        Assert.Equal(0, response.IndexedFiles);
        Assert.Equal(1, response.SkippedFiles);
    }

    private static async Task VerifyChangeRequiresForce(
        Func<ChunkingConfig, ChunkingConfig> update,
        string manifestKey,
        string indexedValue,
        string currentValue)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Unique Heading\n\nParagraph text.", cancellationToken);
        var initialConfig = TestConfig(temp.Path);
        var initialRepository = CreateRepository(initialConfig);
        await CreateIndexer(initialConfig, initialRepository)
            .ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);

        var changedConfig = initialConfig with { Chunking = update(initialConfig.Chunking) };
        var changedRepository = CreateRepository(changedConfig);
        var changedIndexer = CreateIndexer(changedConfig, changedRepository);
        var exception = await Assert.ThrowsAsync<IndexCompatibilityException>(
            () => changedIndexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken));

        Assert.Contains(manifestKey, exception.Message);
        Assert.Contains($"indexed '{indexedValue}'", exception.Message);
        Assert.Contains($"current '{currentValue}'", exception.Message);
        Assert.Contains("force=true", exception.Message);

        var response = await changedIndexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, true), cancellationToken);
        var manifest = await ReadManifestAsync(changedConfig, cancellationToken);

        Assert.Equal(1, response.IndexedFiles);
        Assert.Equal(currentValue, manifest[manifestKey]);
    }

    private static SqliteTestServices CreateRepository(LocalVectorSearchMcpConfig config)
        => SqliteTestServices.Create(config);

    private static KnowledgeBaseIndexer CreateIndexer(
        LocalVectorSearchMcpConfig config,
        SqliteTestServices repository)
        => new(
            config,
            new MarkdownDocumentLoader(),
            new MarkdownElementParser(),
            new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()),
            new FakeEmbeddingProvider(3),
            repository.Initializer,
            repository.DocumentStore,
            repository.Manifest);

    private static async Task<IReadOnlyDictionary<string, string>> ReadManifestAsync(
        LocalVectorSearchMcpConfig config,
        CancellationToken cancellationToken)
    {
        await using var db = new SqliteConnectionFactory(config).Open();
        var command = db.CreateCommand();
        command.CommandText = "select key, value from index_manifest";
        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            manifest.Add(reader.GetString(0), reader.GetString(1));
        }

        return manifest;
    }

    private static async Task DeleteManifestKeyAsync(
        LocalVectorSearchMcpConfig config,
        string key,
        CancellationToken cancellationToken)
    {
        await using var db = new SqliteConnectionFactory(config).Open();
        var command = db.CreateCommand();
        command.CommandText = "delete from index_manifest where key = $key";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LocalVectorSearchMcpConfig TestConfig(string root) => new()
    {
        Storage = new StorageConfig { Path = Path.Combine(root, ".local-vector-search-mcp", "index.db") },
        Embedding = new EmbeddingConfig { Model = "model", Dimensions = 3 },
        Chunking = new ChunkingConfig
        {
            MaxChunkBytes = 4096,
            MaxElements = 20,
            IncludeHeadingContext = true,
            IncludeFrontMatter = true
        },
        KnowledgeBase = new KnowledgeBaseConfig { Root = root }
    };
}
