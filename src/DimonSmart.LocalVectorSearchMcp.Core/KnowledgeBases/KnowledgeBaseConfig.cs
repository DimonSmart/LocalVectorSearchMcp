namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

public sealed record KnowledgeBaseConfig
{
    public string Name { get; init; } = "";
    public string Root { get; init; } = "";
    public List<string> Include { get; init; } = ["**/*.md"];
    public List<string> Exclude { get; init; } = ["**/bin/**", "**/obj/**", "**/.git/**", "**/node_modules/**", "**/.local-vector-search-mcp/**"];
}
