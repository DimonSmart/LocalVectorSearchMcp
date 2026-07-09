namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

public sealed record StatusResponse(
    string DatabasePath,
    string SchemaVersion,
    string ChunkerVersion,
    string EmbeddingTextBuilderVersion,
    string EmbeddingModel,
    int? EmbeddingDimensions,
    ProjectIndexStatus Project);
