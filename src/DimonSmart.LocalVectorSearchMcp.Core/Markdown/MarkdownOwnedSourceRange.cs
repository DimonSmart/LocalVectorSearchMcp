namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public static class MarkdownOwnedSourceRange
{
    public static SourceRange GetOwnedSourceRange(
        int sourceLength,
        IReadOnlyList<MarkdownElement> elements,
        MarkdownElement element)
    {
        if (element.Kind != MarkdownElementKind.Heading)
        {
            return new SourceRange(element.SourceStart, element.SourceLength);
        }

        var boundary = elements
            .Where(candidate =>
                candidate.Kind == MarkdownElementKind.Heading
                && candidate.SourceStart > element.SourceStart
                && candidate.HeadingLevel <= element.HeadingLevel)
            .OrderBy(candidate => candidate.SourceStart)
            .FirstOrDefault();

        var end = boundary?.SourceStart ?? sourceLength;
        return new SourceRange(element.SourceStart, end - element.SourceStart);
    }
}
