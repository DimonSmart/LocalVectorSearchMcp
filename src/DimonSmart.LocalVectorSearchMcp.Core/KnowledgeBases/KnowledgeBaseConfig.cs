namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

public sealed record KnowledgeBaseConfig
{
    public string Root { get; init; } = "";
    public IReadOnlyList<string> Include { get; init; } = ["**/*.md"];
    public IReadOnlyList<string> Exclude { get; init; } = [];
    public bool AllowWrites { get; init; }
    public bool WatchFiles { get; init; }
}
