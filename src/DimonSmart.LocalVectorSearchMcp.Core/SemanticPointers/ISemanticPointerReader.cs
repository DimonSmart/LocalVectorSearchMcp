namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public interface ISemanticPointerReader
{
    Task<MarkdownSlice> ReadAsync(
        string path,
        SemanticAnchor anchor,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken);
}
