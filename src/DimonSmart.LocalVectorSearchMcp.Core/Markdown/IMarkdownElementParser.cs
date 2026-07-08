namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public interface IMarkdownElementParser
{
    IReadOnlyList<MarkdownElement> Parse(MarkdownSourceDocument document);
}
