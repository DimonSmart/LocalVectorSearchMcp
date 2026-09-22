using System.Text;
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
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Fakes;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkbenchIntegrationTests
{
    [Fact]
    public async Task Schema2Manifest_RequiresForcedRebuildToSchema4()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "notes.md"),
            "# Notes\n\nBody.\n",
            cancellationToken);
        var services = CreateServices(temp.Path);
        await services.Repository.Initializer.InitializeAsync(cancellationToken);
        await services.Repository.Manifest.WriteCurrentManifestAsync(cancellationToken);
        await using (var db = new SqliteConnectionFactory(services.Config).Open())
        {
            var command = db.CreateCommand();
            command.CommandText = "update index_manifest set value = '2' where key = 'schema_version'";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await Assert.ThrowsAsync<DimonSmart.LocalVectorSearchMcp.Core.Exceptions.IndexCompatibilityException>(() =>
            services.Indexer.ReindexAsync(
                new ReindexRequest(ReindexScope.Changed, false),
                cancellationToken));
        await services.Indexer.ReindexAsync(
            new ReindexRequest(ReindexScope.Changed, true),
            cancellationToken);

        await using var currentDb = new SqliteConnectionFactory(services.Config).Open();
        var current = currentDb.CreateCommand();
        current.CommandText = "select value from index_manifest where key = 'schema_version'";
        Assert.Equal("4", Convert.ToString(await current.ExecuteScalarAsync(cancellationToken)));
    }

    [Fact]
    public async Task Mutations_CreatePatchMoveDelete_ReportPendingSynchronization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);

        var created = await services.Mutations.CreateAsync(
            "chapters/one.md",
            "# Original Heading\n\nold-marker body.\n",
            cancellationToken);
        Assert.False(created.IndexSynchronized);
        Assert.NotEmpty(await services.Repository.FullTextSearch.SearchAsync(
            "old-marker", 10, cancellationToken));
        await Assert.ThrowsAsync<WorkspaceMutationException>(() => services.Mutations.CreateAsync(
            "chapters/one.md", "duplicate", cancellationToken));

        var slice = await services.Reader.ReadSliceAsync(
            "chapters/one.md",
            new Core.SemanticPointers.SemanticPointer("1.p1"),
            10,
            12000,
            cancellationToken);
        Assert.NotNull(slice.SourceHash);
        var paragraphAnchor = new SemanticAnchor(
            new SemanticPointer("1.p1"),
            SemanticFingerprint.Compute(slice.Elements[0].Text)).ToString();
        await Assert.ThrowsAsync<WorkspaceMutationException>(() => services.Mutations.PatchAsync(
            new PatchRequest(
                "chapters/one.md",
                [new PatchOperation(PatchOperationKind.Delete, "9.p1")]),
            cancellationToken));
        Assert.Contains(
            "old-marker body.",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "chapters", "one.md"),
                cancellationToken));
        var patched = await services.Mutations.PatchAsync(
            new PatchRequest(
                "chapters/one.md",
                [new PatchOperation(PatchOperationKind.Replace, paragraphAnchor, "new-marker body.")]),
            cancellationToken);
        Assert.False(patched.IndexSynchronized);
        Assert.Empty(await services.Repository.FullTextSearch.SearchAsync(
            "old-marker", 10, cancellationToken));
        Assert.NotEmpty(await services.Repository.FullTextSearch.SearchAsync(
            "new-marker", 10, cancellationToken));

        var moved = await services.Mutations.MoveAsync(
            new MoveRequest("chapters/one.md", "chapters/two.md", patched.SourceHash!),
            cancellationToken);
        Assert.False(moved.IndexSynchronized);
        var indexedPaths = await services.Repository.DocumentStore.GetDocumentHashesAsync(cancellationToken);
        Assert.DoesNotContain("chapters/one.md", indexedPaths.Keys);
        Assert.Contains("chapters/two.md", indexedPaths.Keys);

        var deleted = await services.Mutations.DeleteAsync(
            new DeleteRequest("chapters/two.md", moved.SourceHash!),
            cancellationToken);
        Assert.False(deleted.IndexSynchronized);
        Assert.Empty(await services.Repository.DocumentStore.GetDocumentHashesAsync(cancellationToken));
        Assert.Empty(await services.Repository.FullTextSearch.SearchAsync(
            "new-marker", 10, cancellationToken));
    }

    [Fact]
    public async Task Patch_RejectsChangedTargetAndPreservesManualEdit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);
        await services.Mutations.CreateAsync(
            "notes.md", "# Notes\n\nOriginal.\n", cancellationToken);
        var anchor = new SemanticAnchor(
            new SemanticPointer("1.p1"),
            SemanticFingerprint.Compute("Original.")).ToString();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "notes.md"),
            "# Notes\n\nHuman edit.\n",
            cancellationToken);

        await Assert.ThrowsAsync<SemanticAnchorConflictException>(() => services.Mutations.PatchAsync(
            new PatchRequest(
                "notes.md",
                [new PatchOperation(PatchOperationKind.Replace, anchor, "LLM edit.")]),
            cancellationToken));

        Assert.Equal(
            "# Notes\n\nHuman edit.\n",
            await File.ReadAllTextAsync(Path.Combine(temp.Path, "notes.md"), cancellationToken));
    }

    [Fact]
    public async Task MoveAndDelete_RejectStaleHashesAndMoveRejectsExistingDestination()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);
        var first = await services.Mutations.CreateAsync(
            "first.md", "# First\n\nBody.\n", cancellationToken);
        await services.Mutations.CreateAsync(
            "second.md", "# Second\n\nBody.\n", cancellationToken);

        await Assert.ThrowsAsync<DocumentConflictException>(() => services.Mutations.MoveAsync(
            new MoveRequest("first.md", "third.md", "stale"),
            cancellationToken));
        await Assert.ThrowsAsync<DocumentConflictException>(() => services.Mutations.DeleteAsync(
            new DeleteRequest("first.md", "stale"),
            cancellationToken));
        await Assert.ThrowsAsync<WorkspaceMutationException>(() => services.Mutations.MoveAsync(
            new MoveRequest("first.md", "second.md", first.SourceHash!),
            cancellationToken));

        Assert.True(File.Exists(Path.Combine(temp.Path, "first.md")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "second.md")));
    }

    [Fact]
    public async Task DisabledWrites_ReturnControlledErrorWithoutChangingSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path, allowWrites: false);

        var exception = await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            services.Mutations.CreateAsync("blocked.md", "text", cancellationToken));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(temp.Path, "blocked.md")));
    }

    [Fact]
    public async Task IndexFailure_LeavesSourceAndMarksDirtyStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path, provider: new FailingEmbeddingProvider());

        var response = await services.Mutations.CreateAsync(
            "source.md", "# Source\n\nBody.\n", cancellationToken);

        Assert.False(response.IndexSynchronized);
        Assert.True(File.Exists(Path.Combine(temp.Path, "source.md")));
        Assert.Equal(1, services.State.GetStatus().PendingFiles);
        Assert.Contains("embedding unavailable", services.State.GetStatus().LastError);
        var status = await new SqliteIndexStatusReader(
            new SqliteConnectionFactory(services.Config),
            services.Config,
            services.State).GetStatusAsync(cancellationToken);
        Assert.Equal(1, status.Synchronization!.PendingFiles);
    }

    [Fact]
    public async Task FailedIndexing_DoesNotBlockCurrentSourceReadOrOutline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var provider = new SwitchableEmbeddingProvider();
        var services = CreateServices(temp.Path, provider: provider);

        var created = await services.Mutations.CreateAsync(
            "source.md",
            "# Source\n\nold-marker\n",
            cancellationToken);
        var originalRead = await services.Reader.ReadSliceAsync(
            "source.md",
            new SemanticAnchor(new SemanticPointer("document")),
            20,
            12_000,
            cancellationToken);
        var target = Assert.Single(
            originalRead.Elements,
            element => element.Text == "old-marker");

        provider.Fail = true;
        var patched = await services.Mutations.PatchAsync(
            new PatchRequest(
                "source.md",
                [
                    new PatchOperation(
                        PatchOperationKind.ReplaceElement,
                        target.Pointer,
                        "new-marker")
                ]),
            cancellationToken);

        var currentRead = await services.Reader.ReadSliceAsync(
            "source.md",
            new SemanticAnchor(new SemanticPointer("document")),
            20,
            12_000,
            cancellationToken);
        var currentOutline = await services.Navigation.GetOutlineAsync(
            "source.md",
            cancellationToken);
        var oldSearch = await services.Search.SearchAsync(
            new SearchRequest("old-marker", SearchMode.Lexical, 10),
            cancellationToken);
        var newSearch = await services.Search.SearchAsync(
            new SearchRequest("new-marker", SearchMode.Lexical, 10),
            cancellationToken);

        Assert.Equal(patched.SourceHash, currentRead.SourceHash);
        Assert.Equal(patched.SourceHash, currentOutline.SourceHash);
        Assert.Contains("new-marker", currentRead.Markdown, StringComparison.Ordinal);
        var staleResult = Assert.Single(oldSearch.Results);
        Assert.Equal(created.SourceHash, staleResult.IndexedSourceHash);
        Assert.Empty(newSearch.Results);
        Assert.Equal(1, services.State.GetStatus().PendingFiles);
        Assert.Contains(
            "embedding unavailable",
            services.State.GetStatus().LastError,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_PreservesBomCrLfAndUntouchedWhitespace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);
        var source = "# Notes\r\n\r\nFirst.  \r\n\r\nSecond.\r\n";
        var bytes = Encoding.UTF8.Preamble.ToArray()
            .Concat(new UTF8Encoding(false).GetBytes(source))
            .ToArray();
        var path = Path.Combine(temp.Path, "notes.md");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        await services.Synchronizer.ReconcileAsync("notes.md", cancellationToken);
        var document = await new MarkdownDocumentLoader().LoadFileAsync(
            services.Config.KnowledgeBase, "notes.md", cancellationToken);
        var target = new MarkdownElementParser().Parse(document)
            .Single(element => element.Pointer.Value == "1.p2");
        var targetAnchor = SemanticAnchor.FromElement(target).ToString();

        await services.Mutations.PatchAsync(
            new PatchRequest(
                "notes.md",
                [new PatchOperation(PatchOperationKind.Replace, targetAnchor, "Changed.")]),
            cancellationToken);

        var result = await File.ReadAllBytesAsync(path, cancellationToken);
        Assert.True(result.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var text = Encoding.UTF8.GetString(result.AsSpan(Encoding.UTF8.Preamble.Length));
        Assert.Equal("# Notes\r\n\r\nFirst.  \r\n\r\nChanged.\r\n", text);
    }

    [Theory]
    [InlineData(SearchMode.Lexical)]
    [InlineData(SearchMode.Semantic)]
    [InlineData(SearchMode.Hybrid)]
    public async Task SearchScope_IsAppliedBeforeTopK_InEveryMode(SearchMode mode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "chapters"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "sources"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "chapters", "a.md"),
            "# ScopeTerm\n\nshared term chapter\n",
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "sources", "b.md"),
            "# Source\n\nshared term source\n",
            cancellationToken);
        var services = CreateServices(temp.Path);
        await services.Indexer.ReindexAsync(
            new ReindexRequest(ReindexScope.Changed, false),
            cancellationToken);

        var response = await services.Search.SearchAsync(
            new SearchRequest(
                mode == SearchMode.Lexical ? "shared" : "anything",
                mode,
                1,
                ["chapters/**/*.md"],
                []),
            cancellationToken);

        Assert.Single(response.Results);
        Assert.Equal("chapters/a.md", response.Results[0].Path);
        var indexedHashes = await services.Repository.DocumentStore
            .GetDocumentHashesAsync(cancellationToken);
        Assert.Equal(
            indexedHashes["chapters/a.md"],
            response.Results[0].IndexedSourceHash);

        var excluded = await services.Search.SearchAsync(
            new SearchRequest(
                mode == SearchMode.Lexical ? "shared" : "anything",
                mode,
                1,
                ["**/*.md"],
                ["sources/**"]),
            cancellationToken);
        Assert.Single(excluded.Results);
        Assert.Equal("chapters/a.md", excluded.Results[0].Path);
        Assert.NotEmpty(await services.Repository.FullTextSearch.SearchAsync(
            "ScopeTerm", 10, cancellationToken));
    }

    [Fact]
    public async Task Navigation_ListsAssetsAndBuildsNestedOutline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "assets", "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "assets", "images", "cover.webp"),
            [1, 2, 3],
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "BOOK.md"),
            "# Book\n\n## Part\n\n### Chapter\n",
            cancellationToken);
        var services = CreateServices(temp.Path);

        var files = await services.Navigation.ListFilesAsync(null, null, cancellationToken);
        var outline = await services.Navigation.GetOutlineAsync("BOOK.md", cancellationToken);

        Assert.Contains(files.Files, file =>
            file.RelativePath == "assets/images/cover.webp" && file.Kind == WorkspaceFileKind.Asset);
        Assert.Contains(files.Files, file =>
            file.RelativePath == "BOOK.md" && file.Kind == WorkspaceFileKind.Markdown);
        Assert.Single(outline.Headings);
        Assert.Matches(@"^1~[0-9a-f]{16}~[0-9a-f]{16}$", outline.Headings[0].Pointer);
        Assert.Equal("Part", outline.Headings[0].Children[0].Title);
        Assert.Matches(
            @"^1\.1\.1~[0-9a-f]{16}~[0-9a-f]{16}$",
            outline.Headings[0].Children[0].Children[0].Pointer);
    }

    [Fact]
    public async Task Watcher_ReconcilesExternalCreateRenameAndDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var provider = new CountingEmbeddingProvider();
        var services = CreateServices(temp.Path, watchFiles: true, provider: provider);
        await services.Repository.Initializer.InitializeAsync(cancellationToken);
        await services.Repository.Manifest.WriteCurrentManifestAsync(cancellationToken);
        var scheduler = new WorkspaceIndexSynchronizationScheduler(
            services.Synchronizer,
            services.State,
            NullLogger<WorkspaceIndexSynchronizationScheduler>.Instance);
        var watcher = new MarkdownWorkspaceWatcher(
            services.Config,
            scheduler,
            services.State,
            NullLogger<MarkdownWorkspaceWatcher>.Instance);
        await scheduler.StartAsync(cancellationToken);
        await watcher.StartAsync(cancellationToken);
        try
        {
            await Task.Delay(250, cancellationToken);
            var first = Path.Combine(temp.Path, "external.md");
            await File.WriteAllTextAsync(first, "# External\n\ncreated-marker\n", cancellationToken);
            await WaitUntilAsync(async () =>
                (await services.Repository.DocumentStore.GetDocumentHashesAsync(cancellationToken))
                .ContainsKey("external.md"));
            var callsAfterCreate = provider.Calls;

            await File.WriteAllTextAsync(first, "# External\n\nedit-one\n", cancellationToken);
            await File.WriteAllTextAsync(first, "# External\n\nedit-two\n", cancellationToken);
            await File.WriteAllTextAsync(first, "# External\n\nfinal-marker\n", cancellationToken);
            var finalDocument = await new MarkdownDocumentLoader().LoadFileAsync(
                services.Config.KnowledgeBase,
                "external.md",
                cancellationToken);
            await WaitUntilAsync(async () =>
            {
                var hashes = await services.Repository.DocumentStore.GetDocumentHashesAsync(cancellationToken);
                return hashes.GetValueOrDefault("external.md") == finalDocument.SourceHash;
            });
            Assert.Equal(callsAfterCreate + 1, provider.Calls);

            var renamed = Path.Combine(temp.Path, "renamed.md");
            File.Move(first, renamed);
            await WaitUntilAsync(async () =>
            {
                var hashes = await services.Repository.DocumentStore.GetDocumentHashesAsync(cancellationToken);
                return !hashes.ContainsKey("external.md") && hashes.ContainsKey("renamed.md");
            });

            File.Delete(renamed);
            await WaitUntilAsync(async () =>
                !(await services.Repository.DocumentStore.GetDocumentHashesAsync(cancellationToken))
                .ContainsKey("renamed.md"));
        }
        finally
        {
            await watcher.StopAsync(CancellationToken.None);
            await scheduler.StopAsync(CancellationToken.None);
            watcher.Dispose();
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(100);
        }

        Assert.Fail("Timed out waiting for watcher reconciliation.");
    }

    private static WorkbenchServices CreateServices(
        string root,
        bool watchFiles = false,
        bool allowWrites = true,
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
                AllowWrites = allowWrites,
                WatchFiles = watchFiles
            }
        };
        var repository = SqliteTestServices.Create(config);
        var loader = new MarkdownDocumentLoader();
        var parser = new MarkdownElementParser();
        var chunker = new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder());
        provider ??= new FakeEmbeddingProvider();
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
            operationGate);
        var mutations = new WorkspaceMutationService(
            config,
            guard,
            loader,
            parser,
            new ImmediateIndexSynchronizationScheduler(
                synchronizer,
                state));
        var reader = new SourceMarkdownSliceReader(
            config,
            guard,
            loader,
            parser);
        var navigation = new WorkspaceNavigationService(
            config, guard, loader, parser);
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
            config,
            repository,
            state,
            synchronizer,
            reader,
            mutations,
            navigation,
            indexer,
            search);
    }

    private sealed record WorkbenchServices(
        LocalVectorSearchMcpConfig Config,
        SqliteTestServices Repository,
        InMemoryIndexSynchronizationState State,
        WorkspaceIndexSynchronizer Synchronizer,
        SourceMarkdownSliceReader Reader,
        WorkspaceMutationService Mutations,
        WorkspaceNavigationService Navigation,
        KnowledgeBaseIndexer Indexer,
        KnowledgeSearchService Search);

    private sealed class SwitchableEmbeddingProvider : IEmbeddingProvider
    {
        public bool Fail { get; set; }

        public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new EmbeddingProviderException("embedding unavailable");
            }

            IReadOnlyList<EmbeddingVector> result = texts
                .Select(_ => new EmbeddingVector([0.5f, 0.2f, 0.1f]))
                .ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class FailingEmbeddingProvider : IEmbeddingProvider
    {
        public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
            => throw new DimonSmart.LocalVectorSearchMcp.Core.Embeddings.EmbeddingProviderException(
                "embedding unavailable");
    }

    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            IReadOnlyList<EmbeddingVector> result = texts
                .Select(_ => new EmbeddingVector([0.5f, 0.2f, 0.1f]))
                .ToList();
            return Task.FromResult(result);
        }
    }
}
