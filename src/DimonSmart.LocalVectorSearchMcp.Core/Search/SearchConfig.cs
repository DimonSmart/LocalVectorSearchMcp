namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record SearchConfig
{
    public SearchMode DefaultMode { get; init; } = SearchMode.Hybrid;
    public int SemanticCandidatePoolSize { get; init; } = 50;
    public int LexicalCandidatePoolSize { get; init; } = 50;
    public int MaxResults { get; init; } = 10;
    public int RrfK { get; init; } = 60;
}
