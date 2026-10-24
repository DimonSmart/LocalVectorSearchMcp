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
    [property: Description(
        "Optional root-relative directory path or exact file path. " +
        "A directory includes files beneath it. This is not a partial filename prefix; " +
        "use includeGlob for filename pattern matching.")]
    string? PathPrefix = null,
    [property: Description(
        "Optional glob pattern matched against root-relative file paths. " +
        "Use patterns such as '*.md', '**/*.md', or 'qa-*.md' to filter filenames. " +
        "When pathPrefix is also specified, both filters must match.")]
    string? IncludeGlob = null);

public sealed record OutlineToolRequest(string Path);

public sealed record CreatePdfBookToolRequest(
    string Path,
    [property: Description("Optional root-relative .pdf destination. Omitted uses the Markdown source name with a .pdf extension.")]
    string? OutputPath = null,
    PdfBookPageFormat PageFormat = PdfBookPageFormat.A5,
    PdfBookStyle Style = PdfBookStyle.Compact,
    [property: Description("Image optimization: screen (96 DPI, JPEG 65), standard (150 DPI, JPEG 82; default), print (300 DPI, JPEG 95). Print is not PDF/X; original images may be retained.")]
    PdfExportProfile Profile = PdfExportProfile.Standard);

public sealed record LinksToolRequest(
    string? Path = null,
    string Direction = "both",
    int Depth = 1,
    bool IncludeBroken = false,
    bool BrokenOnly = false,
    int PageSize = 100,
    string? Cursor = null);
