using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

public sealed class ImmediateIndexSynchronizationScheduler(
    IWorkspaceIndexSynchronizer synchronizer) : IWorkspaceIndexSynchronizationScheduler
{
    public void Schedule(string relativePath)
    {
        try
        {
            synchronizer.ReconcileAsync(relativePath, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // The production scheduler records reconciliation failures in synchronization state.
        }
    }
}
