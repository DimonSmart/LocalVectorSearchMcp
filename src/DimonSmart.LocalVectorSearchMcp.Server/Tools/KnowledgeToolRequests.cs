using System.ComponentModel;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

public sealed record ReindexToolRequest(
    [property: Description("Changed skips unchanged files; all reindexes them even if source hashes match.")]
    ReindexScope Scope = ReindexScope.Changed,
    [property: Description("Allow rebuilding an incompatible index; independent from scope=all.")]
    bool Force = false);

public sealed record SearchToolRequest(
    string Query,
    [property: Description("Optional search mode; omitted uses the configured default.")]
    SearchMode? Mode = null,
    [property: Description("Maximum result count; positive and capped at 50.")]
    int? TopK = null,
    [property: Description("Optional root-relative glob patterns restricting searched paths.")]
    IReadOnlyList<string>? IncludeGlobs = null,
    [property: Description("Optional root-relative glob patterns excluding searched paths.")]
    IReadOnlyList<string>? ExcludeGlobs = null);

public sealed record ReadToolRequest(
    string Path,
    [property: Description("Semantic pointer to start from. Omit or use \"document\" to start from the document root.")]
    string? Pointer = null,
    int? MaxElements = null,
    int? MaxBytes = null);

public sealed record PatchToolOperation(PatchOperationKind Kind, string Pointer, string? Markdown = null);

public sealed record PatchToolRequest(
    string Path,
    IReadOnlyList<PatchToolOperation> Operations);

public sealed record CreateToolRequest(string Path, string Markdown);

public sealed record MoveToolRequest(string SourcePath, string TargetPath, string ExpectedSourceHash);

public sealed record DeleteToolRequest(string Path, string ExpectedSourceHash);

public sealed record ListFilesToolRequest(
    [property: Description("Optional root-relative path prefix.")]
    string? PathPrefix = null,
    [property: Description("Optional glob filter for the listed files.")]
    string? IncludeGlob = null);

public sealed record OutlineToolRequest(string Path);
