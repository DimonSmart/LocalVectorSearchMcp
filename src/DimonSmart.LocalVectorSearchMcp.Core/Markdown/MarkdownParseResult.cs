namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

/// <summary>Elements and parse-wide metadata for one source revision.</summary>
public sealed record MarkdownParseResult(
    IReadOnlyList<MarkdownElement> Elements,
    IReadOnlySet<string> ReservedPointers);
