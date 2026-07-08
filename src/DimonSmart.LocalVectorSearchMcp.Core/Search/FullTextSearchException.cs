namespace DimonSmart.LocalVectorSearchMcp.Core;

public class FullTextSearchException(string message, Exception? inner = null) : Exception(message, inner);
