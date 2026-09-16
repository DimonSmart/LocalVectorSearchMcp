using System.Globalization;
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
        var documentPointer = new SemanticPointer("document");
        var elements = new List<MarkdownElement>
        {
            new(
                document.RelativePath,
                documentPointer,
                MarkdownElementKind.Document,
                "",
                1,
                1,
                0,
                null,
                documentPointer,
                0,
                0)
        };
        var syntax = Markdig.Markdown.Parse(document.Markdown, Pipeline);
        var headingStack = new List<HeadingContext>();
        var paragraphCounts = new Dictionary<string, int>();
        var codeCounts = new Dictionary<string, int>();
        var rootParagraph = 0;
        var rootCode = 0;
        var rootSectionCount = 0;
        var currentSection = documentPointer;
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
                var frontMatterPointer = new SemanticPointer("frontmatter");
                elements.Add(new MarkdownElement(
                    document.RelativePath,
                    frontMatterPointer,
                    MarkdownElementKind.FrontMatter,
                    text,
                    startLine,
                    endLine,
                    0,
                    null,
                    frontMatterPointer,
                    start,
                    length));
                continue;
            }

            if (block is HeadingBlock heading)
            {
                var level = heading.Level;
                while (headingStack.Count > 0 && headingStack[^1].Level >= level)
                {
                    headingStack.RemoveAt(headingStack.Count - 1);
                }

                var ordinal = headingStack.Count == 0
                    ? ++rootSectionCount
                    : ++headingStack[^1].ChildCount;
                var pointerValue = headingStack.Count == 0
                    ? ordinal.ToString(CultureInfo.InvariantCulture)
                    : $"{headingStack[^1].Pointer.Value}.{ordinal.ToString(CultureInfo.InvariantCulture)}";
                var sectionPointer = new SemanticPointer(pointerValue);
                var title = ExtractHeadingTitle(text);
                headingStack.Add(new HeadingContext(level, sectionPointer, title));
                currentSection = sectionPointer;
                currentHeadingPath = string.Join(" > ", headingStack.Select(item => item.Title));
                elements.Add(new MarkdownElement(
                    document.RelativePath,
                    sectionPointer,
                    MarkdownElementKind.Heading,
                    text,
                    startLine,
                    endLine,
                    level,
                    currentHeadingPath,
                    sectionPointer,
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
                currentSection,
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
        SemanticPointer section,
        Dictionary<string, int> counts,
        ref int rootCount,
        string prefix)
    {
        if (section.Value == "document")
        {
            rootCount++;
            return new SemanticPointer($"{prefix}{rootCount}");
        }

        counts[section.Value] = counts.GetValueOrDefault(section.Value) + 1;
        return new SemanticPointer($"{section.Value}.{prefix}{counts[section.Value]}");
    }

    private sealed class HeadingContext(int level, SemanticPointer pointer, string title)
    {
        public int Level { get; } = level;
        public SemanticPointer Pointer { get; } = pointer;
        public string Title { get; } = title;
        public int ChildCount { get; set; }
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*$")]
    private static partial Regex AtxHeadingRegex();

    [GeneratedRegex(@"\s+#+\s*$")]
    private static partial Regex ClosingHashesRegex();
}
