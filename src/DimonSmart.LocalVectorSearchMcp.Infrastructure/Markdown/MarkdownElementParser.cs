using System.Text.RegularExpressions;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

public sealed partial class MarkdownElementParser : IMarkdownElementParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    public IReadOnlyList<MarkdownElement> Parse(MarkdownSourceDocument document)
    {
        var elements = new List<MarkdownElement>
        {
            new(
                document.RelativePath,
                new SemanticPointer("document"),
                MarkdownElementKind.Document,
                "",
                1,
                1,
                0,
                null,
                0,
                0)
        };
        var syntax = Markdig.Markdown.Parse(document.Markdown, Pipeline);
        var sectionCounters = new int[6];
        var headingTitles = new string?[6];
        var paragraphCounts = new Dictionary<string, int>();
        var codeCounts = new Dictionary<string, int>();
        var rootParagraph = 0;
        var rootCode = 0;
        var currentSection = "";
        string? currentHeadingPath = null;

        foreach (var block in syntax.Descendants().OfType<Block>())
        {
            if (block is not (HeadingBlock or ParagraphBlock or FencedCodeBlock or CodeBlock or YamlFrontMatterBlock))
            {
                continue;
            }

            // Container descendants can expose the same source range. Index only leaf editing units.
            if (block is ParagraphBlock paragraph && paragraph.Parent is QuoteBlock
                && paragraph.Span == paragraph.Parent.Span)
            {
                continue;
            }

            var (start, length) = GetSpan(block, document.Markdown.Length);
            if (length <= 0)
            {
                continue;
            }

            var text = document.Markdown.Substring(start, length);
            var startLine = block.Line + 1;
            var endLine = block.Line + CountLineBreaks(text);

            if (block is YamlFrontMatterBlock)
            {
                elements.Add(new MarkdownElement(
                    document.RelativePath,
                    new SemanticPointer("frontmatter"),
                    MarkdownElementKind.FrontMatter,
                    text,
                    startLine,
                    endLine,
                    0,
                    null,
                    start,
                    length));
                continue;
            }

            if (block is HeadingBlock heading)
            {
                var level = heading.Level;
                sectionCounters[level - 1]++;
                for (var index = level; index < sectionCounters.Length; index++)
                {
                    sectionCounters[index] = 0;
                    headingTitles[index] = null;
                }

                currentSection = string.Join('.', sectionCounters.Take(level).Where(value => value > 0));
                var title = ExtractHeadingTitle(text);
                headingTitles[level - 1] = title;
                currentHeadingPath = string.Join(
                    " > ",
                    headingTitles.Take(level).Where(value => !string.IsNullOrWhiteSpace(value)));
                elements.Add(new MarkdownElement(
                    document.RelativePath,
                    new SemanticPointer(currentSection),
                    MarkdownElementKind.Heading,
                    text,
                    startLine,
                    endLine,
                    level,
                    currentHeadingPath,
                    start,
                    length));
                continue;
            }

            var isCode = block is CodeBlock;
            var pointer = isCode
                ? NextPointer(currentSection, codeCounts, ref rootCode, "code")
                : NextPointer(currentSection, paragraphCounts, ref rootParagraph, "p");
            elements.Add(new MarkdownElement(
                document.RelativePath,
                pointer,
                isCode ? MarkdownElementKind.CodeBlock : MarkdownElementKind.Paragraph,
                text,
                startLine,
                endLine,
                0,
                currentHeadingPath,
                start,
                length));
        }

        return elements.OrderBy(element => element.SourceStart)
            .ThenBy(element => element.Kind == MarkdownElementKind.Document ? 0 : 1)
            .ToList();
    }

    private static (int Start, int Length) GetSpan(Block block, int sourceLength)
    {
        var start = Math.Clamp(block.Span.Start, 0, sourceLength);
        var end = Math.Clamp(block.Span.End, start - 1, sourceLength - 1);
        return (start, end >= start ? end - start + 1 : 0);
    }

    private static int CountLineBreaks(string value)
        => value.Count(character => character == '\n');

    private static string ExtractHeadingTitle(string source)
    {
        var firstLine = source.Split(['\r', '\n'], 2)[0];
        var match = AtxHeadingRegex().Match(firstLine);
        return match.Success
            ? ClosingHashesRegex().Replace(match.Groups[1].Value.Trim(), "").Trim()
            : firstLine.Trim();
    }

    private static SemanticPointer NextPointer(
        string section,
        Dictionary<string, int> counts,
        ref int rootCount,
        string prefix)
    {
        if (string.IsNullOrEmpty(section))
        {
            rootCount++;
            return new SemanticPointer($"{prefix}{rootCount}");
        }

        counts[section] = counts.GetValueOrDefault(section) + 1;
        return new SemanticPointer($"{section}.{prefix}{counts[section]}");
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*$")]
    private static partial Regex AtxHeadingRegex();

    [GeneratedRegex(@"\s+#+\s*$")]
    private static partial Regex ClosingHashesRegex();
}
