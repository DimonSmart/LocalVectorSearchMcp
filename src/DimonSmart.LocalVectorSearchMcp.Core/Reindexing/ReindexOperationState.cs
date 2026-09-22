namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

public sealed record ReindexProgress(
    int ProcessedFiles,
    int? TotalFiles,
    string? CurrentPath,
    bool IsDestructiveRebuild = false);

public sealed record ReindexCurrentOperation(
    ReindexScope Scope,
    bool Force,
    DateTimeOffset StartedAtUtc,
    int ProcessedFiles = 0,
    int? TotalFiles = null,
    string? CurrentPath = null,
    bool IsDestructiveRebuild = false);

public sealed record ReindexLastOperation(
    ReindexScope Scope,
    bool Force,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Outcome,
    ReindexResponse? Result,
    string? Error);

public sealed record ReindexStatus(
    bool IsRunning,
    ReindexCurrentOperation? Current,
    ReindexLastOperation? Last);

public sealed record ReindexStartResponse(
    bool Started,
    ReindexCurrentOperation Current);

public interface IReindexStateReader
{
    ReindexStatus GetStatus();
}

public interface IReindexCoordinator : IReindexStateReader
{
    ReindexStartResponse TryStart(ReindexRequest request);
}
