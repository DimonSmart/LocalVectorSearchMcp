namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IMarkdownChunker
{
    IReadOnlyList<MarkdownChunk> BuildChunks(MarkdownSourceDocument document, IReadOnlyList<MarkdownElement> elements);
}
