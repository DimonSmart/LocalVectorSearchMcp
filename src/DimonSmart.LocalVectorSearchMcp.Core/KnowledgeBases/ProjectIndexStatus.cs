namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

public sealed record ProjectIndexStatus(string Root, int Documents, int Chunks, DateTimeOffset? LastIndexedAtUtc);
