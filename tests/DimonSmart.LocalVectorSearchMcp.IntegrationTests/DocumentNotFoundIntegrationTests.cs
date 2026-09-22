using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class DocumentNotFoundIntegrationTests
{
    [Fact]
    public async Task ExistingDocumentOperations_MissingSource_UseDocumentNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);

        async Task AssertMissingAsync(Func<Task> action)
        {
            var exception = await Assert.ThrowsAsync<DocumentNotFoundException>(action);
            Assert.Equal("Document 'missing.md' was not found.", exception.Message);
        }

        await AssertMissingAsync(async () =>
            await services.Reader.ReadSliceAsync(
                "missing.md",
                new SemanticPointer("document"),
                20,
                12_000,
                cancellationToken));
        await AssertMissingAsync(async () =>
            await services.Navigation.GetOutlineAsync(
                "missing.md",
                cancellationToken));
        await AssertMissingAsync(async () =>
            await services.Mutations.PatchAsync(
                new PatchRequest(
                    "missing.md",
                    [
                        new PatchOperation(
                            PatchOperationKind.InsertAfter,
                            "document",
                            "Text.")
                    ]),
                cancellationToken));
        await AssertMissingAsync(async () =>
            await services.Mutations.MoveAsync(
                new MoveRequest(
                    "missing.md",
                    "target.md",
                    "unused"),
                cancellationToken));
        await AssertMissingAsync(async () =>
            await services.Mutations.DeleteAsync(
                new DeleteRequest(
                    "missing.md",
                    "unused"),
                cancellationToken));
    }

    [Fact]
    public async Task ExistingDocument_MissingPointer_RemainsPointerSpecific()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "notes.md"),
            "# Notes\n\nText.\n",
            cancellationToken);
        var services = CreateServices(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticPointerNotFoundException>(
            () => services.Reader.ReadSliceAsync(
                "notes.md",
                new SemanticPointer("1.p99"),
                20,
                12_000,
                cancellationToken));

        Assert.Contains(
            "Pointer '1.p99' was not found",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Document 'notes.md' was not found",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPath_RemainsAccessError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var services = CreateServices(temp.Path);

        await Assert.ThrowsAsync<KnowledgeBaseAccessException>(
            () => services.Reader.ReadSliceAsync(
                "../outside.md",
                new SemanticPointer("document"),
                20,
                12_000,
                cancellationToken));
    }

    [Fact]
    public async Task LoadRace_FileNotFound_IsMappedToDocumentNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "notes.md"),
            "# Notes\n",
            cancellationToken);
        var config = CreateConfig(temp.Path);
        var reader = new SourceMarkdownSliceReader(
            config,
            new KnowledgeBasePathGuard(config),
            new ThrowingMarkdownDocumentLoader(
                new FileNotFoundException("removed during load")),
            new MarkdownElementParser());

        var exception = await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => reader.ReadSliceAsync(
                "notes.md",
                new SemanticPointer("document"),
                20,
                12_000,
                cancellationToken));

        Assert.Equal("Document 'notes.md' was not found.", exception.Message);
    }

    [Fact]
    public async Task ArbitraryIoError_IsNotMappedToDocumentNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "notes.md"),
            "# Notes\n",
            cancellationToken);
        var config = CreateConfig(temp.Path);
        var reader = new SourceMarkdownSliceReader(
            config,
            new KnowledgeBasePathGuard(config),
            new ThrowingMarkdownDocumentLoader(
                new IOException("storage failure")),
            new MarkdownElementParser());

        var exception = await Assert.ThrowsAsync<IOException>(
            () => reader.ReadSliceAsync(
                "notes.md",
                new SemanticPointer("document"),
                20,
                12_000,
                cancellationToken));

        Assert.Equal("storage failure", exception.Message);
    }

    private static Services CreateServices(string root)
    {
        var config = CreateConfig(root);
        var guard = new KnowledgeBasePathGuard(config);
        var loader = new MarkdownDocumentLoader();
        var parser = new MarkdownElementParser();

        return new Services(
            new SourceMarkdownSliceReader(
                config,
                guard,
                loader,
                parser),
            new WorkspaceNavigationService(
                config,
                guard,
                loader,
                parser),
            new WorkspaceMutationService(
                config,
                guard,
                loader,
                parser,
                new NoOpSynchronizationScheduler()));
    }

    private static LocalVectorSearchMcpConfig CreateConfig(string root)
        => new()
        {
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = root,
                AllowWrites = true
            }
        };

    private sealed record Services(
        SourceMarkdownSliceReader Reader,
        WorkspaceNavigationService Navigation,
        WorkspaceMutationService Mutations);

    private sealed class NoOpSynchronizationScheduler
        : IWorkspaceIndexSynchronizationScheduler
    {
        public void Schedule(string relativePath)
        {
        }
    }

    private sealed class ThrowingMarkdownDocumentLoader(Exception exception)
        : IMarkdownDocumentLoader
    {
        public Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(
            KnowledgeBaseConfig knowledgeBase,
            CancellationToken cancellationToken)
            => Task.FromException<IReadOnlyList<MarkdownSourceDocument>>(exception);

        public Task<MarkdownSourceDocument> LoadFileAsync(
            KnowledgeBaseConfig knowledgeBase,
            string relativePath,
            CancellationToken cancellationToken)
            => Task.FromException<MarkdownSourceDocument>(exception);
    }
}
