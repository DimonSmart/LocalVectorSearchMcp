namespace DimonSmart.LocalVectorSearchMcp.Core;

public class VectorIndexException(string message, Exception? inner = null) : Exception(message, inner);
