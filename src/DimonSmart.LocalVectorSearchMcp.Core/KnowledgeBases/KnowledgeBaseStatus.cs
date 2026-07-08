namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record KnowledgeBaseStatus(string Name, string Root, int Documents, int Chunks, DateTimeOffset? LastIndexedAtUtc);
