namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

public sealed record ProjectIndexStatus(int Documents, int Chunks, DateTimeOffset? LastIndexedAtUtc);
