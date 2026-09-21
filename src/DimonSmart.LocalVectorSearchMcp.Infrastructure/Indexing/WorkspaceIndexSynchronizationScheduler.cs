using System.Collections.Concurrent;
using System.Threading.Channels;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;

public sealed class WorkspaceIndexSynchronizationScheduler(
    IWorkspaceIndexSynchronizer synchronizer,
    ILogger<WorkspaceIndexSynchronizationScheduler> logger)
    : BackgroundService, IWorkspaceIndexSynchronizationScheduler
{
    private readonly Channel<string> pending = Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string, byte> scheduled =
        new(StringComparer.OrdinalIgnoreCase);

    public void Schedule(string relativePath)
    {
        if (scheduled.TryAdd(relativePath, 0))
        {
            pending.Writer.TryWrite(relativePath);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var path in pending.Reader.ReadAllAsync(stoppingToken))
        {
            scheduled.TryRemove(path, out _);
            try
            {
                await synchronizer.ReconcileAsync(path, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not synchronize committed Markdown file {Path}",
                    path);
            }
        }
    }
}
