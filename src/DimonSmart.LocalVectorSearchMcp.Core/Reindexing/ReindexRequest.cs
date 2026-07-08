namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record ReindexRequest(string? KnowledgeBase, ReindexScope Scope, bool Force);
