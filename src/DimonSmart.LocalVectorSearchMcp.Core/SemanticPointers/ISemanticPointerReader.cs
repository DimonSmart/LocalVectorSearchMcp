namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public interface ISemanticPointerReader
{
    Task<MarkdownSlice> ReadAsync(string? knowledgeBase, string path, SemanticPointer pointer, int maxElements, int maxBytes, CancellationToken cancellationToken);
}
