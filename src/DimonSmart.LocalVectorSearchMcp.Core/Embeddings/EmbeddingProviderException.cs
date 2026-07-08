namespace DimonSmart.LocalVectorSearchMcp.Core;

public class EmbeddingProviderException(string message, Exception? inner = null) : Exception(message, inner);
