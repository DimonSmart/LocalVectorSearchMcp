namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record LocalVectorSearchMcpConfig
{
    public ServerConfig Server { get; init; } = new();
    public StorageConfig Storage { get; init; } = new();
    public EmbeddingConfig Embedding { get; init; } = new();
    public ChunkingConfig Chunking { get; init; } = new();
    public SearchConfig Search { get; init; } = new();
    public List<KnowledgeBaseConfig> KnowledgeBases { get; init; } = [];
}
