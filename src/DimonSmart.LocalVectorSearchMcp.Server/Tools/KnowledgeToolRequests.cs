using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

public sealed record ReindexToolRequest(
    ReindexScope Scope = ReindexScope.Changed,
    bool Force = false);

public sealed record SearchToolRequest(
    string Query,
    SearchMode? Mode = null,
    int? TopK = null);

public sealed record ReadToolRequest(
    string Path,
    string Pointer,
    int? MaxElements = null,
    int? MaxBytes = null);
