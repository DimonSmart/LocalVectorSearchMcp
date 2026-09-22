using System.Collections.Concurrent;
using System.Threading.Channels;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;

public sealed class WorkspaceIndexSynchronizationScheduler(
    IWorkspaceIndexSynchronizer synchronizer,
    IIndexSynchronizationState state,
    ILogger<WorkspaceIndexSynchronizationScheduler> logger)
    : BackgroundService, IWorkspaceIndexSynchronizationScheduler
{
    private const string PendingMessage = "Index synchronization is pending.";

    private readonly Channel<string> pending = Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string, PathWorkState> paths =
        new(StringComparer.OrdinalIgnoreCase);

    public void Schedule(string relativePath)
    {
        var generation = state.MarkDirty(relativePath, PendingMessage);
        var pathState = paths.GetOrAdd(
            relativePath,
            _ => new PathWorkState());

        var enqueue = false;
        lock (pathState.Gate)
        {
            pathState.LatestGeneration = Math.Max(
                pathState.LatestGeneration,
                generation);
            if (!pathState.Queued && !pathState.Running)
            {
                pathState.Queued = true;
                enqueue = true;
            }
        }

        if (enqueue)
        {
            pending.Writer.TryWrite(relativePath);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var path in pending.Reader.ReadAllAsync(stoppingToken))
        {
            if (!paths.TryGetValue(path, out var pathState))
            {
                continue;
            }

            long generation;
            lock (pathState.Gate)
            {
                pathState.Queued = false;
                pathState.Running = true;
                generation = pathState.LatestGeneration;
            }

            var succeeded = false;
            string? error = null;
            try
            {
                await synchronizer.ReconcileAsync(path, stoppingToken);
                succeeded = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                logger.LogWarning(
                    exception,
                    "Could not synchronize committed Markdown file {Path}",
                    path);
            }

            var enqueueAgain = false;
            lock (pathState.Gate)
            {
                pathState.Running = false;
                if (succeeded)
                {
                    state.MarkSynchronized(path, generation);
                }
                else
                {
                    state.MarkFailed(
                        path,
                        generation,
                        error ?? "Index synchronization failed.");
                }

                if (pathState.LatestGeneration != generation
                    && !pathState.Queued)
                {
                    pathState.Queued = true;
                    enqueueAgain = true;
                }
            }

            if (enqueueAgain)
            {
                pending.Writer.TryWrite(path);
            }
        }
    }

    private sealed class PathWorkState
    {
        public object Gate { get; } = new();
        public long LatestGeneration { get; set; }
        public bool Queued { get; set; }
        public bool Running { get; set; }
    }
}
