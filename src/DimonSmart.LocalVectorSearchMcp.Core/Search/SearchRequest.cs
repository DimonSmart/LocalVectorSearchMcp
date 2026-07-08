namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record SearchRequest(string Query, string? KnowledgeBase, SearchMode? Mode, int? TopK);
