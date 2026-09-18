using System.ComponentModel;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

public sealed record ReindexToolRequest(
    ReindexScope Scope = ReindexScope.Changed,
    bool Force = false);

public sealed record SearchToolRequest(
    string Query,
    SearchMode? Mode = null,
    int? TopK = null,
    IReadOnlyList<string>? IncludeGlobs = null,
    IReadOnlyList<string>? ExcludeGlobs = null);

public sealed record ReadToolRequest(
    string Path,
    [property: Description("Semantic pointer to start from. Omit or use \"document\" to start from the document root.")]
    string? Pointer = null,
    int? MaxElements = null,
    int? MaxBytes = null);

public sealed record PatchToolOperation(string Kind, string Pointer, string? Markdown = null);

public sealed record PatchToolRequest(
    string Path,
    string ExpectedSourceHash,
    IReadOnlyList<PatchToolOperation> Operations);

public sealed record CreateToolRequest(string Path, string Markdown);

public sealed record MoveToolRequest(string SourcePath, string TargetPath, string ExpectedSourceHash);

public sealed record DeleteToolRequest(string Path, string ExpectedSourceHash);

public sealed record ListFilesToolRequest(string? PathPrefix = null, string? IncludeGlob = null);

public sealed record OutlineToolRequest(string Path);
