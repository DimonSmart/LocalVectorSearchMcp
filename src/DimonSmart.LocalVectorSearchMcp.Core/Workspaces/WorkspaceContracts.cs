using System.Text.Json;
using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

public enum PatchOperationKind
{
    Replace,
    InsertBefore,
    InsertAfter,
    Delete
}

public sealed record PatchOperation(PatchOperationKind Kind, string Pointer, string? Markdown = null);

public sealed record PatchRequest(
    string Path,
    string ExpectedSourceHash,
    IReadOnlyList<PatchOperation> Operations);

public sealed record MoveRequest(string SourcePath, string TargetPath, string ExpectedSourceHash);

public sealed record DeleteRequest(string Path, string ExpectedSourceHash);

public sealed record MutationResponse(
    string Path,
    string? SourceHash,
    bool IndexSynchronized,
    string? IndexError = null,
    string? PreviousPath = null);

public interface IWorkspaceMutationService
{
    Task<MutationResponse> PatchAsync(PatchRequest request, CancellationToken cancellationToken);
    Task<MutationResponse> CreateAsync(string path, string markdown, CancellationToken cancellationToken);
    Task<MutationResponse> MoveAsync(MoveRequest request, CancellationToken cancellationToken);
    Task<MutationResponse> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken);
}

[JsonConverter(typeof(WorkspaceFileKindJsonConverter))]
public enum WorkspaceFileKind
{
    Markdown,
    Asset
}

public sealed class WorkspaceFileKindJsonConverter : JsonConverter<WorkspaceFileKind>
{
    public override WorkspaceFileKind Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => reader.GetString()?.ToLowerInvariant() switch
        {
            "markdown" => WorkspaceFileKind.Markdown,
            "asset" => WorkspaceFileKind.Asset,
            _ => throw new JsonException("Workspace file kind must be markdown or asset.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        WorkspaceFileKind value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            WorkspaceFileKind.Markdown => "markdown",
            WorkspaceFileKind.Asset => "asset",
            _ => throw new JsonException($"Unsupported workspace file kind '{value}'.")
        });
}

public sealed record WorkspaceFile(
    string RelativePath,
    WorkspaceFileKind Kind,
    long Size,
    DateTimeOffset LastWriteTimeUtc);

public sealed record WorkspaceFileList(IReadOnlyList<WorkspaceFile> Files);

public sealed record OutlineNode(
    string Pointer,
    int Level,
    string Title,
    IReadOnlyList<OutlineNode> Children);

public sealed record MarkdownOutline(string Path, string SourceHash, IReadOnlyList<OutlineNode> Headings);

public interface IWorkspaceNavigationService
{
    Task<WorkspaceFileList> ListFilesAsync(
        string? pathPrefix,
        string? includeGlob,
        CancellationToken cancellationToken);
    Task<MarkdownOutline> GetOutlineAsync(string path, CancellationToken cancellationToken);
}

public interface IWorkspaceIndexSynchronizer
{
    Task<bool> ReconcileAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed record IndexSynchronizationStatus(
    int PendingFiles,
    IReadOnlyList<string> Paths,
    string? LastError);

public interface IIndexSynchronizationState
{
    void MarkDirty(string relativePath, string error);
    void MarkSynchronized(string relativePath);
    IndexSynchronizationStatus GetStatus();
}

public sealed class DocumentConflictException(string message) : Exception(message);

public sealed class WorkspaceMutationException(string message) : Exception(message);
