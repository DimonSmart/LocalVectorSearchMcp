using System.Collections.Concurrent;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;

public sealed class MarkdownWorkspaceWatcher(
    LocalVectorSearchMcpConfig config,
    IWorkspaceIndexSynchronizationScheduler scheduler,
    IIndexSynchronizationState state,
    ILogger<MarkdownWorkspaceWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);
    private readonly ConcurrentDictionary<string, DateTimeOffset> pending =
        new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? watcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.KnowledgeBase.WatchFiles) return;

        watcher = new FileSystemWatcher(config.KnowledgeBase.Root)
        {
            IncludeSubdirectories = true,
            Filter = "*",
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
        };
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
        watcher.EnableRaisingEvents = true;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var threshold = DateTimeOffset.UtcNow - Debounce;
            var due = pending.Where(item => item.Value <= threshold)
                .Select(item => item.Key)
                .ToList();
            foreach (var path in due)
            {
                if (pending.TryRemove(path, out _))
                {
                    scheduler.Schedule(path);
                }
            }
        }
    }

    public override void Dispose()
    {
        watcher?.Dispose();
        base.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
        => Queue(args.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        Queue(args.OldFullPath);
        Queue(args.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        var message = args.GetException().Message;
        state.MarkDirty("<watcher>", message);
        logger.LogWarning("Markdown file watcher error: {Error}", message);
    }

    private void Queue(string absolutePath)
    {
        if (!Path.GetExtension(absolutePath).Equals(
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(
                config.KnowledgeBase.Root,
                absolutePath)
            .Replace('\', '/');
        if (relativePath.StartsWith("../", StringComparison.Ordinal)
            || !MatchesConfiguredSource(relativePath))
        {
            return;
        }

        pending[relativePath] = DateTimeOffset.UtcNow;
    }

    private bool MatchesConfiguredSource(string relativePath)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var include in config.KnowledgeBase.Include)
        {
            matcher.AddInclude(include);
        }

        foreach (var exclude in config.KnowledgeBase.Exclude)
        {
            matcher.AddExclude(exclude);
        }

        return matcher.Match(relativePath).HasMatches;
    }
}
