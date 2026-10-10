namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

/// <summary>Exact source ownership established by one Markdig parse.</summary>
public sealed record MarkdownElementSourceMap(
    SourceRange SubtreeRange,
    SourceRange ReplaceRange,
    SourceRange DeleteRange,
    IReadOnlyList<SourceRange> OwnSegments,
    int BeforePosition,
    int AfterPosition,
    string? ParentPointer = null,
    string? ContainerId = null,
    int Depth = 0,
    int Indent = 0,
    string? MarkerStyle = null);
