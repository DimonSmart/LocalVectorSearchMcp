namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record SearchRequest(
    string Query,
    SearchMode? Mode,
    int? TopK,
    IReadOnlyList<string>? IncludeGlobs = null,
    IReadOnlyList<string>? ExcludeGlobs = null);
