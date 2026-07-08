namespace DimonSmart.LocalVectorSearchMcp.Core.Storage;

public sealed record StorageConfig
{
    public string Path { get; init; } = "./.local-vector-search-mcp/index.db";
}
