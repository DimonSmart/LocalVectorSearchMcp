namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

internal static class WorkspaceMutationGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            Gate.Release();
        }
    }
}
