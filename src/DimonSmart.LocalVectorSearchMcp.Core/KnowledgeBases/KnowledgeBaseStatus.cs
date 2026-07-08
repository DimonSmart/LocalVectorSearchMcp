namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

public sealed record KnowledgeBaseStatus(string Name, string Root, int Documents, int Chunks, DateTimeOffset? LastIndexedAtUtc);
