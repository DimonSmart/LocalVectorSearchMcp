namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed class SemanticPointerFormatException : Exception
{
    public SemanticPointerFormatException(string message) : base(message)
    {
    }

    public SemanticPointerFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
