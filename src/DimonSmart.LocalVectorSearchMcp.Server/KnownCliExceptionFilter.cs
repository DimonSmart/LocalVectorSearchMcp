using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Server;

internal static class KnownCliExceptionFilter
{
    public static bool IsKnown(Exception exception)
        => exception is ConfigurationException
            or KnowledgeBaseAccessException
            or EmbeddingProviderException
            or IndexCompatibilityException
            or IndexNotReadyException
            or VectorIndexException
            or FullTextSearchException
            or SemanticPointerNotFoundException;
}
