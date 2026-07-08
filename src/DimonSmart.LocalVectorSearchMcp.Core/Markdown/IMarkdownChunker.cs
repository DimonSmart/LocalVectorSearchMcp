namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public interface IMarkdownChunker
{
    IReadOnlyList<MarkdownChunk> BuildChunks(MarkdownSourceDocument document, IReadOnlyList<MarkdownElement> elements);
}
