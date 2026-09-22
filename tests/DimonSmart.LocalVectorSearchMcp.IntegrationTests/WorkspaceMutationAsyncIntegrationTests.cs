using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkspaceMutationAsyncIntegrationTests
{
    [Fact]
    public async Task Create_ReturnsBeforeIndexReconciliationCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var harness = CreateHarness(temp.Path);
        await harness.Scheduler.StartAsync(cancellationToken);

        try
        {
            var response = await harness.Service.CreateAsync(
                "created.md",
                "# Created\n\nBody.\n",
                cancellationToken);
            var startedPath = await harness.Synchronizer.Started.Task.WaitAsync(
                cancellationToken);

            Assert.Equal("created.md", startedPath);
            Assert.False(harness.Synchronizer.IsReleased);
            Assert.True(File.Exists(Path.Combine(temp.Path, "created.md")));
            Assert.Equal(
                "# Created\n\nBody.\n",
                await File.ReadAllTextAsync(
                    Path.Combine(temp.Path, "created.md"),
                    cancellationToken));
            AssertPending(response);
        }
        finally
        {
            harness.Synchronizer.Release();
            await harness.Scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Patch_ReturnsBeforeIndexReconciliationCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        await File.WriteAllTextAsync(
            path,
            "# Chapter\n\nOriginal.\n",
            cancellationToken);
        var harness = CreateHarness(temp.Path);
        await harness.Scheduler.StartAsync(cancellationToken);

        try
        {
            var response = await harness.Service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [
                        new PatchOperation(
                            PatchOperationKind.Replace,
                            Anchor("1.p1", "Original."),
                            "Changed.")
                    ]),
                cancellationToken);
            var startedPath = await harness.Synchronizer.Started.Task.WaitAsync(
                cancellationToken);

            Assert.Equal("chapter.md", startedPath);
            Assert.False(harness.Synchronizer.IsReleased);
            Assert.Equal(
                "# Chapter\n\nChanged.\n",
                await File.ReadAllTextAsync(path, cancellationToken));
            AssertPending(response);
        }
        finally
        {
            harness.Synchronizer.Release();
            await harness.Scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Move_ReturnsBeforeIndexReconciliationCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "source.md"),
            "# Source\n\nBody.\n",
            cancellationToken);
        var harness = CreateHarness(temp.Path);
        var source = await new MarkdownDocumentLoader().LoadFileAsync(
            harness.Config.KnowledgeBase,
            "source.md",
            cancellationToken);
        await harness.Scheduler.StartAsync(cancellationToken);

        try
        {
            var response = await harness.Service.MoveAsync(
                new MoveRequest(
                    "source.md",
                    "target.md",
                    source.SourceHash),
                cancellationToken);
            await harness.Synchronizer.Started.Task.WaitAsync(cancellationToken);

            Assert.False(harness.Synchronizer.IsReleased);
            Assert.False(File.Exists(Path.Combine(temp.Path, "source.md")));
            Assert.True(File.Exists(Path.Combine(temp.Path, "target.md")));
            Assert.Equal("source.md", response.PreviousPath);
            AssertPending(response);
        }
        finally
        {
            harness.Synchronizer.Release();
            await harness.Scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Delete_ReturnsBeforeIndexReconciliationCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "delete.md"),
            "# Delete\n\nBody.\n",
            cancellationToken);
        var harness = CreateHarness(temp.Path);
        var source = await new MarkdownDocumentLoader().LoadFileAsync(
            harness.Config.KnowledgeBase,
            "delete.md",
            cancellationToken);
        await harness.Scheduler.StartAsync(cancellationToken);

        try
        {
            var response = await harness.Service.DeleteAsync(
                new DeleteRequest(
                    "delete.md",
                    source.SourceHash),
                cancellationToken);
            var startedPath = await harness.Synchronizer.Started.Task.WaitAsync(
                cancellationToken);

            Assert.Equal("delete.md", startedPath);
            Assert.False(harness.Synchronizer.IsReleased);
            Assert.False(File.Exists(Path.Combine(temp.Path, "delete.md")));
            AssertPending(response);
        }
        finally
        {
            harness.Synchronizer.Release();
            await harness.Scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackgroundReconciliationFailure_DoesNotStopScheduler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var synchronizer = new FailFirstSynchronizer();
        var scheduler = new WorkspaceIndexSynchronizationScheduler(
            synchronizer,
            NullLogger<WorkspaceIndexSynchronizationScheduler>.Instance);
        await scheduler.StartAsync(cancellationToken);

        try
        {
            scheduler.Schedule("first.md");
            scheduler.Schedule("second.md");

            var processed = await synchronizer.SecondProcessed.Task.WaitAsync(
                cancellationToken);

            Assert.Equal("second.md", processed);
            Assert.Equal(2, synchronizer.CallCount);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private static MutationHarness CreateHarness(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = root,
                AllowWrites = true
            }
        };
        var synchronizer = new BlockingSynchronizer();
        var scheduler = new WorkspaceIndexSynchronizationScheduler(
            synchronizer,
            NullLogger<WorkspaceIndexSynchronizationScheduler>.Instance);
        var service = new WorkspaceMutationService(
            config,
            new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(),
            new MarkdownElementParser(),
            scheduler,
            new InMemoryIndexSynchronizationState());

        return new MutationHarness(
            config,
            service,
            scheduler,
            synchronizer);
    }

    private static void AssertPending(MutationResponse response)
    {
        Assert.False(response.IndexSynchronized);
        Assert.Null(response.IndexError);
    }

    private static string Anchor(string pointer, string exactText)
        => new SemanticAnchor(
            new SemanticPointer(pointer),
            SemanticFingerprint.Compute(exactText)).ToString();

    private sealed record MutationHarness(
        LocalVectorSearchMcpConfig Config,
        WorkspaceMutationService Service,
        WorkspaceIndexSynchronizationScheduler Scheduler,
        BlockingSynchronizer Synchronizer);

    private sealed class BlockingSynchronizer : IWorkspaceIndexSynchronizer
    {
        private readonly TaskCompletionSource<bool> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsReleased => release.Task.IsCompleted;

        public async Task<bool> ReconcileAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(relativePath);
            await release.Task.WaitAsync(cancellationToken);
            return true;
        }

        public void Release() => release.TrySetResult(true);
    }

    private sealed class FailFirstSynchronizer : IWorkspaceIndexSynchronizer
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public TaskCompletionSource<string> SecondProcessed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> ReconcileAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
            {
                throw new InvalidOperationException("expected reconciliation failure");
            }

            SecondProcessed.TrySetResult(relativePath);
            return Task.FromResult(true);
        }
    }
}
