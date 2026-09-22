using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ReindexCoordinatorTests
{
    [Fact]
    public async Task Start_DuplicateAndSuccess_AreTrackedWithoutStartingSecondPipeline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var indexer = new ControlledIndexer();
        var coordinator = CreateCoordinator(indexer);
        await coordinator.StartAsync(cancellationToken);

        try
        {
            var first = coordinator.TryStart(
                new ReindexRequest(ReindexScope.Changed, false));
            Assert.True(first.Started);

            await indexer.Started.Task.WaitAsync(cancellationToken);
            var second = coordinator.TryStart(
                new ReindexRequest(ReindexScope.All, true));

            Assert.False(second.Started);
            Assert.Equal(first.Current, second.Current);
            Assert.Equal(1, indexer.CallCount);

            var running = coordinator.GetStatus();
            Assert.True(running.IsRunning);
            Assert.Equal(1, running.Current!.ProcessedFiles);
            Assert.Equal(3, running.Current.TotalFiles);
            Assert.Equal("chapter.md", running.Current.CurrentPath);

            var expected = new ReindexResponse(3, 1, 2, 0, 4, null);
            indexer.Complete(expected);
            await WaitUntilIdleAsync(coordinator, cancellationToken);

            var completed = coordinator.GetStatus();
            Assert.False(completed.IsRunning);
            Assert.Null(completed.Current);
            Assert.Equal("succeeded", completed.Last!.Outcome);
            Assert.Equal(expected, completed.Last.Result);
            Assert.Null(completed.Last.Error);
        }
        finally
        {
            await coordinator.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Failure_ClearsRunningState_AndAllowsRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstIndexer = new ControlledIndexer();
        var coordinator = CreateCoordinator(firstIndexer);
        await coordinator.StartAsync(cancellationToken);

        try
        {
            Assert.True(coordinator.TryStart(
                new ReindexRequest(ReindexScope.Changed, false)).Started);
            await firstIndexer.Started.Task.WaitAsync(cancellationToken);

            firstIndexer.Fail(new InvalidOperationException("boom"));
            await WaitUntilIdleAsync(coordinator, cancellationToken);

            var failed = coordinator.GetStatus();
            Assert.False(failed.IsRunning);
            Assert.Equal("failed", failed.Last!.Outcome);
            Assert.Equal("boom", failed.Last.Error);

            var retry = coordinator.TryStart(
                new ReindexRequest(ReindexScope.All, false));
            Assert.True(retry.Started);
        }
        finally
        {
            firstIndexer.Complete(
                new ReindexResponse(0, 0, 0, 0, 0, null));
            await coordinator.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task HostCancellation_MarksOperationCancelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var indexer = new ControlledIndexer();
        var coordinator = CreateCoordinator(indexer);
        await coordinator.StartAsync(cancellationToken);

        Assert.True(coordinator.TryStart(
            new ReindexRequest(ReindexScope.Changed, false)).Started);
        await indexer.Started.Task.WaitAsync(cancellationToken);

        await coordinator.StopAsync(cancellationToken);

        var status = coordinator.GetStatus();
        Assert.False(status.IsRunning);
        Assert.Equal("cancelled", status.Last!.Outcome);
        Assert.Null(status.Last.Result);
        Assert.Null(status.Last.Error);
    }

    private static ReindexCoordinator CreateCoordinator(
        IKnowledgeBaseIndexer indexer)
        => new(
            indexer,
            NullLogger<ReindexCoordinator>.Instance);

    private static async Task WaitUntilIdleAsync(
        IReindexStateReader stateReader,
        CancellationToken cancellationToken)
    {
        while (stateReader.GetStatus().IsRunning)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class ControlledIndexer : IKnowledgeBaseIndexer
    {
        private readonly TaskCompletionSource<ReindexResponse> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async Task<ReindexResponse> ReindexAsync(
            ReindexRequest request,
            CancellationToken cancellationToken,
            IProgress<ReindexProgress>? progress = null)
        {
            CallCount++;
            progress?.Report(new ReindexProgress(
                1,
                3,
                "chapter.md"));
            Started.TrySetResult();
            return await completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(ReindexResponse response)
            => completion.TrySetResult(response);

        public void Fail(Exception exception)
            => completion.TrySetException(exception);
    }
}
