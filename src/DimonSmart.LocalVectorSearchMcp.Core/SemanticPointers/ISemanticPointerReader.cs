namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public interface ISemanticPointerReader
{
    Task<MarkdownSlice> ReadAsync(string path, SemanticPointer pointer, int maxElements, int maxBytes, CancellationToken cancellationToken);
}
