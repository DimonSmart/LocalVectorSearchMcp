using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;

namespace DimonSmart.LocalVectorSearchMcp.Core.Configuration;

public sealed record LocalVectorSearchMcpConfig
{
    public ServerConfig Server { get; init; } = new();
    public StorageConfig Storage { get; init; } = new();
    public EmbeddingConfig Embedding { get; init; } = new();
    public ChunkingConfig Chunking { get; init; } = new();
    public SearchConfig Search { get; init; } = new();
    public KnowledgeBaseConfig KnowledgeBase { get; init; } = new();
}
