namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record StatusResponse(
    string DatabasePath,
    string SchemaVersion,
    string ChunkerVersion,
    string EmbeddingTextBuilderVersion,
    string EmbeddingModel,
    int? EmbeddingDimensions,
    IReadOnlyList<KnowledgeBaseStatus> KnowledgeBases);
