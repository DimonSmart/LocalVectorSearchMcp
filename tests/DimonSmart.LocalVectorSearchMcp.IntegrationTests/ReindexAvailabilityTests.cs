using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol.Protocol;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ReindexAvailabilityTests
{
    [Fact]
    public async Task DestructiveRebuild_ReadAndSearchReturnControlledErrorImmediately()
    {
        var coordinator = new FixedReindexCoordinator(
            new ReindexStatus(
                true,
                new ReindexCurrentOperation(
                    ReindexScope.All,
                    true,
                    DateTimeOffset.UtcNow,
                    IsDestructiveRebuild: true),
                null));
        var tools = new KnowledgeMcpTools(
            coordinator,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);

        var read = await tools.ReadAsync(
            new ReadToolRequest("book.md"),
            CancellationToken.None);
        var search = await tools.SearchAsync(
            new SearchToolRequest("query"),
            CancellationToken.None);

        AssertRebuildError(read);
        AssertRebuildError(search);
    }

    [Fact]
    public async Task IndexOperationGate_SerializesFullReindexAndTargetedReconciliation()
    {
        var gate = new IndexOperationGate();
        var fullEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFull = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var targetedEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var full = gate.RunAsync(
            async () =>
            {
                fullEntered.TrySetResult();
                await releaseFull.Task;
                return true;
            },
            CancellationToken.None);

        await fullEntered.Task;

        var targeted = gate.RunAsync(
            () =>
            {
                targetedEntered.TrySetResult();
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.False(targetedEntered.Task.IsCompleted);

        releaseFull.TrySetResult();
        await full;
        await targeted;

        Assert.True(targetedEntered.Task.IsCompletedSuccessfully);
    }

    private static void AssertRebuildError(CallToolResult result)
    {
        Assert.True(result.IsError is true);
        var text = Assert.IsType<TextContentBlock>(
            Assert.Single(result.Content)).Text;
        Assert.Contains(
            "Index rebuild is currently in progress",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "indexing.isRunning = false",
            text,
            StringComparison.Ordinal);
    }

    private sealed class FixedReindexCoordinator(ReindexStatus status)
        : IReindexCoordinator
    {
        public ReindexStatus GetStatus() => status;

        public ReindexStartResponse TryStart(ReindexRequest request)
            => throw new NotSupportedException();
    }
}
