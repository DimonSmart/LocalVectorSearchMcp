namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public class FullTextSearchException(string message, Exception? inner = null) : Exception(message, inner);
