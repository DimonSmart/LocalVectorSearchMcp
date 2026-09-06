namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record SearchPathScope(
    IReadOnlyList<string> IncludeGlobs,
    IReadOnlyList<string> ExcludeGlobs)
{
    public static SearchPathScope All { get; } = new(["**/*.md"], []);
}
