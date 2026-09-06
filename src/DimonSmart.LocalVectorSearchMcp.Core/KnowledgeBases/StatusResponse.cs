namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

public sealed record StatusResponse(
    string DatabasePath,
    string SchemaVersion,
    string ChunkerVersion,
    string EmbeddingTextBuilderVersion,
    string EmbeddingModel,
    int? EmbeddingDimensions,
    ProjectIndexStatus Project,
    IndexSynchronizationStatus? Synchronization = null);
