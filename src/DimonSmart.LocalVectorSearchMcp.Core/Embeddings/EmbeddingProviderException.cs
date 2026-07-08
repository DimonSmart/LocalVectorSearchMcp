namespace DimonSmart.LocalVectorSearchMcp.Core.Embeddings;

public class EmbeddingProviderException(string message, Exception? inner = null) : Exception(message, inner);
