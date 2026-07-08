namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public class VectorIndexException(string message, Exception? inner = null) : Exception(message, inner);
