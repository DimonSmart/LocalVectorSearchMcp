namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record SearchRequest(string Query, string? KnowledgeBase, SearchMode? Mode, int? TopK);
