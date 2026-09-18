using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkbenchHardeningIntegrationTests
{
    [Fact]
    public async Task EmptyDocument_CanBeReadFilledAndImmediatelySearched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);

        var created = await services.Mutations.CreateAsync("chapter.md", "", cancellationToken);
        Assert.True(created.IndexSynchronized);
        var empty = await services.Repository.SliceReader.ReadSliceAsync(
            "chapter.md",
            new SemanticPointer("document"),
            10,
            12000,
            cancellationToken);

        Assert.Equal(created.SourceHash, empty.SourceHash);
        Assert.Equal("document", empty.Pointer);
        Assert.Empty(empty.Elements);
        Assert.Equal("", empty.Markdown);
        Assert.Null(empty.NextPointer);

        var patched = await services.Mutations.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.InsertAfter,
                    "document",
                    "# Chapter 1\n\nhardening-marker")]),
            cancellationToken);

        Assert.True(patched.IndexSynchronized);
        Assert.Equal(
            "# Chapter 1\n\nhardening-marker",
            await File.ReadAllTextAsync(Path.Combine(temp.Path, "chapter.md"), cancellationToken));
        Assert.NotEmpty(await services.Repository.FullTextSearch.SearchAsync(
            "hardening-marker",
            10,
            cancellationToken));
    }

    [Fact]
    public async Task DocumentPatch_AppliesToLatestSourceWithoutClientHash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);
        await services.Mutations.CreateAsync("chapter.md", "", cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "chapter.md"),
            "Human.",
            cancellationToken);

        await services.Mutations.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(PatchOperationKind.InsertAfter, "document", "Agent.")]),
            cancellationToken);

        Assert.Equal(
            "Human.\n\nAgent.",
            await File.ReadAllTextAsync(Path.Combine(temp.Path, "chapter.md"), cancellationToken));
    }

    [Theory]
    [InlineData(SearchMode.Lexical)]
    [InlineData(SearchMode.Semantic)]
    [InlineData(SearchMode.Hybrid)]
    public async Task ScopedSearch_RanksOnlyEligibleChunks(SearchMode mode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "chapters"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "sources"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "chapters", "inside.md"),
            "# Inside\n\nneedle\n",
            cancellationToken);
        for (var index = 0; index < 99; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "sources", $"outside-{index:D2}.md"),
                "# Outside\n\nneedle needle needle needle needle\n",
                cancellationToken);
        }

        var services = CreateServices(temp.Path, new ScopeAwareEmbeddingProvider());
        await services.Indexer.ReindexAsync(
            new ReindexRequest(ReindexScope.Changed, false),
            cancellationToken);

        var response = await services.Search.SearchAsync(
            new SearchRequest("needle", mode, 1, ["chapters/**"], []),
            cancellationToken);

        Assert.Single(response.Results);
        Assert.Equal("chapters/inside.md", response.Results[0].Path);
    }

    [Theory]
    [InlineData(SearchMode.Lexical)]
    [InlineData(SearchMode.Semantic)]
    [InlineData(SearchMode.Hybrid)]
    public async Task ScopedSearch_WithNoEligiblePaths_ReturnsEmpty(SearchMode mode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "notes.md"),
            "# Notes\n\nneedle\n",
            cancellationToken);
        var services = CreateServices(temp.Path, new ScopeAwareEmbeddingProvider());
        await services.Indexer.ReindexAsync(
            new ReindexRequest(ReindexScope.Changed, false),
            cancellationToken);

        var response = await services.Search.SearchAsync(
            new SearchRequest("needle", mode, 5, ["missing/**"], []),
            cancellationToken);

        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task Navigation_SkipsGitDirectoriesBeforeTraversalButKeepsOtherDotFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, ".git", "objects", "aa"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "nested", ".git"));
        Directory.CreateDirectory(Path.Combine(temp.Path, ".github", "workflows"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, ".git", "config"), "secret", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, ".git", "objects", "aa", "object"), "data", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "nested", ".git", "config"), "nested", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, ".editorconfig"), "root = true", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, ".github", "workflows", "ci.yml"), "name: ci", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), "# Notes", cancellationToken);
        var services = CreateServices(temp.Path);

        var files = await services.Navigation.ListFilesAsync(null, null, cancellationToken);
        var gitOnly = await services.Navigation.ListFilesAsync(null, ".git/**", cancellationToken);

        Assert.Contains(files.Files, file => file.RelativePath == ".editorconfig");
        Assert.Contains(files.Files, file => file.RelativePath == ".github/workflows/ci.yml");
        Assert.Contains(files.Files, file => file.RelativePath == "notes.md");
        Assert.DoesNotContain(files.Files, file => file.RelativePath.Split('/').Contains(".git"));
        Assert.Empty(gitOnly.Files);
    }

    private static WorkbenchServices CreateServices(
        string root,
        IEmbeddingProvider? provider = null)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            Storage = new StorageConfig
            {
                Path = Path.Combine(root, ".local-vector-search-mcp", "index.db")
            },
            Embedding = new EmbeddingConfig { Dimensions = 3 },
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = root,
                AllowWrites = true
            }
        };
        var repository = SqliteTestServices.Create(config);
        var loader = new MarkdownDocumentLoader();
        var parser = new MarkdownElementParser();
        var chunker = new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder());
        provider ??= new ScopeAwareEmbeddingProvider();
        var state = new InMemoryIndexSynchronizationState();
        var operationGate = new IndexOperationGate();
        var guard = new KnowledgeBasePathGuard(config);
        var synchronizer = new WorkspaceIndexSynchronizer(
            config,
            guard,
            loader,
            parser,
            chunker,
            provider,
            repository.Initializer,
            repository.Manifest,
            repository.DocumentStore,
            state,
            operationGate);
        var mutations = new WorkspaceMutationService(
            config,
            guard,
            loader,
            parser,
            synchronizer);
        var navigation = new WorkspaceNavigationService(
            config,
            guard,
            loader,
            parser);
        var indexer = new KnowledgeBaseIndexer(
            config,
            loader,
            parser,
            chunker,
            provider,
            repository.Initializer,
            repository.DocumentStore,
            repository.Manifest,
            state,
            operationGate);
        var search = new KnowledgeSearchService(
            config,
            provider,
            repository.VectorSearch,
            repository.FullTextSearch,
            repository.SearchIndexReader,
            repository.SearchIndexReader);
        return new WorkbenchServices(
            repository,
            mutations,
            navigation,
            indexer,
            search);
    }

    private sealed record WorkbenchServices(
        SqliteTestServices Repository,
        WorkspaceMutationService Mutations,
        WorkspaceNavigationService Navigation,
        KnowledgeBaseIndexer Indexer,
        KnowledgeSearchService Search);

    private sealed class ScopeAwareEmbeddingProvider : IEmbeddingProvider
    {
        public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EmbeddingVector> result = texts.Select(text =>
                text.Contains("Path: chapters/", StringComparison.OrdinalIgnoreCase)
                    ? new EmbeddingVector([0f, 1f, 0f])
                    : new EmbeddingVector([1f, 0f, 0f])).ToList();
            return Task.FromResult(result);
        }
    }
}
