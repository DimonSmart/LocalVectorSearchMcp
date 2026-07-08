namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IMarkdownElementParser
{
    IReadOnlyList<MarkdownElement> Parse(MarkdownSourceDocument document);
}
