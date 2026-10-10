using System.ComponentModel;
using System.Text.Json.Serialization;
using DimonSmart.LocalVectorSearchMcp.Core.Serialization;

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
    ReindexOutcome Outcome,
    ReindexResponse? Result,
    string? Error);

[JsonConverter(typeof(StrictJsonStringEnumConverter<ReindexOutcome>))]
public enum ReindexOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

public sealed record ReindexStatus(
    bool IsRunning,
    ReindexCurrentOperation? Current,
    ReindexLastOperation? Last);

public sealed record ReindexStartResponse(
    [property: Description("True if a new operation was queued; false if an operation was already running. This does not mean indexing has completed.")]
    bool Started,
    [property: Description("Active operation, including the previously running operation when started is false. Check kb_status for completion.")]
    ReindexCurrentOperation Current);

public interface IReindexStateReader
{
    ReindexStatus GetStatus();
}

public interface IReindexCoordinator : IReindexStateReader
{
    ReindexStartResponse TryStart(ReindexRequest request);
}
