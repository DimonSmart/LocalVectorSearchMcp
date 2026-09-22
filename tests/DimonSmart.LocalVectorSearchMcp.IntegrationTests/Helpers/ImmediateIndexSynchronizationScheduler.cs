using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

public sealed class ImmediateIndexSynchronizationScheduler(
    IWorkspaceIndexSynchronizer synchronizer,
    IIndexSynchronizationState? state = null) : IWorkspaceIndexSynchronizationScheduler
{
    private const string PendingMessage = "Index synchronization is pending.";

    public void Schedule(string relativePath)
    {
        var generation = state?.MarkDirty(relativePath, PendingMessage);
        try
        {
            synchronizer.ReconcileAsync(relativePath, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (generation is not null)
            {
                state!.MarkSynchronized(relativePath, generation.Value);
            }
        }
        catch (Exception exception)
        {
            if (generation is not null)
            {
                state!.MarkFailed(relativePath, generation.Value, exception.Message);
            }
        }
    }
}
