namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

public sealed record ReindexRequest(string? KnowledgeBase, ReindexScope Scope, bool Force);
