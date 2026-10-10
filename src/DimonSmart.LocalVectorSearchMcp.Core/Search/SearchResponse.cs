using System.ComponentModel;

namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record SearchResponse(
    IReadOnlyList<SearchResultItem> Results,
    [property: Description("Potential search fallback or limitation; null when no warning applies.")]
    string? Warning = null);
