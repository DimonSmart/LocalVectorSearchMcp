using System.Threading.Channels;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DimonSmart.LocalVectorSearchMcp.Server;

public sealed class ReindexCoordinator(
    IKnowledgeBaseIndexer indexer,
    ILogger<ReindexCoordinator> logger)
    : BackgroundService, IReindexCoordinator
{
    private readonly object sync = new();
    private readonly Channel<ReindexRequest> requests =
        Channel.CreateUnbounded<ReindexRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

    private ReindexCurrentOperation? current;
    private ReindexLastOperation? last;

    public ReindexStartResponse TryStart(ReindexRequest request)
    {
        logger.LogInformation(
            "Reindex requested. Scope={Scope}, Force={Force}",
            request.Scope,
            request.Force);

        lock (sync)
        {
            if (current is not null)
            {
                return new ReindexStartResponse(false, current);
            }

            current = new ReindexCurrentOperation(
                request.Scope,
                request.Force,
                DateTimeOffset.UtcNow);

            if (!requests.Writer.TryWrite(request))
            {
                current = null;
                throw new InvalidOperationException(
                    "Unable to schedule reindex operation.");
            }

            return new ReindexStartResponse(true, current);
        }
    }

    public ReindexStatus GetStatus()
    {
        lock (sync)
        {
            return new ReindexStatus(
                current is not null,
                current,
                last);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await requests.Reader.WaitToReadAsync(stoppingToken))
            {
                while (requests.Reader.TryRead(out var request))
                {
                    await RunReindexAsync(request, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunReindexAsync(
        ReindexRequest request,
        CancellationToken stoppingToken)
    {
        ReindexCurrentOperation started;
        lock (sync)
        {
            started = current
                ?? throw new InvalidOperationException(
                    "Reindex coordinator lost the active operation state.");
        }

        logger.LogInformation(
            "Reindex started. Scope={Scope}, Force={Force}",
            request.Scope,
            request.Force);

        var progress = new InlineProgress<ReindexProgress>(ReportProgress);
        try
        {
            var result = await indexer.ReindexAsync(
                request,
                stoppingToken,
                progress);
            var completedAtUtc = DateTimeOffset.UtcNow;
            Complete(
                started,
                completedAtUtc,
                "succeeded",
                result,
                null);

            logger.LogInformation(
                "Reindex completed. Scope={Scope}, Force={Force}, DurationMs={DurationMs}, ScannedFiles={ScannedFiles}, IndexedFiles={IndexedFiles}, SkippedFiles={SkippedFiles}, DeletedFiles={DeletedFiles}, ChunksIndexed={ChunksIndexed}",
                request.Scope,
                request.Force,
                (completedAtUtc - started.StartedAtUtc).TotalMilliseconds,
                result.ScannedFiles,
                result.IndexedFiles,
                result.SkippedFiles,
                result.DeletedFiles,
                result.ChunksIndexed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            var completedAtUtc = DateTimeOffset.UtcNow;
            Complete(
                started,
                completedAtUtc,
                "cancelled",
                null,
                null);
            logger.LogInformation(
                "Reindex cancelled. Scope={Scope}, Force={Force}, DurationMs={DurationMs}",
                request.Scope,
                request.Force,
                (completedAtUtc - started.StartedAtUtc).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            var completedAtUtc = DateTimeOffset.UtcNow;
            Complete(
                started,
                completedAtUtc,
                "failed",
                null,
                exception.Message);
            logger.LogError(
                exception,
                "Reindex failed. Scope={Scope}, Force={Force}, DurationMs={DurationMs}",
                request.Scope,
                request.Force,
                (completedAtUtc - started.StartedAtUtc).TotalMilliseconds);
        }
    }

    private void ReportProgress(ReindexProgress progress)
    {
        var logCandidateCount = false;
        lock (sync)
        {
            if (current is null)
            {
                return;
            }

            logCandidateCount =
                current.TotalFiles is null
                && progress.TotalFiles is not null;

            current = current with
            {
                ProcessedFiles = progress.ProcessedFiles,
                TotalFiles = progress.TotalFiles ?? current.TotalFiles,
                CurrentPath = progress.CurrentPath,
                IsDestructiveRebuild =
                    current.IsDestructiveRebuild
                    || progress.IsDestructiveRebuild
            };
        }

        if (logCandidateCount)
        {
            logger.LogInformation(
                "Reindex candidate file count: {CandidateFileCount}",
                progress.TotalFiles);
        }
    }

    private void Complete(
        ReindexCurrentOperation started,
        DateTimeOffset completedAtUtc,
        string outcome,
        ReindexResponse? result,
        string? error)
    {
        lock (sync)
        {
            last = new ReindexLastOperation(
                started.Scope,
                started.Force,
                started.StartedAtUtc,
                completedAtUtc,
                outcome,
                result,
                error);
            current = null;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
