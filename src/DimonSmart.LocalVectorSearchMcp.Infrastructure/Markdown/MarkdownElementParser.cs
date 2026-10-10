using System.Globalization;
using System.Text.RegularExpressions;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

public sealed partial class MarkdownElementParser : IMarkdownElementParser
{
    public IReadOnlyList<MarkdownElement> Parse(MarkdownSourceDocument document)
       => ParseDetailed(document).Elements;

    public MarkdownParseResult ParseDetailed(MarkdownSourceDocument document)
    {
        var current = ParseWithPipeline(document, MarkdownPipelines.Tables);
        if (!current.Elements.Any(element => element.Kind == MarkdownElementKind.Table))
            return current;

        // Match exact historical paragraph spans, not ordinal guesses: Markdig
        // may previously have parsed the table together with adjacent prose.
        var historical = ParseWithPipeline(document, MarkdownPipelines.Historical);
        var historicalParagraphs = historical.Elements
            .Where(element => element.Kind == MarkdownElementKind.Paragraph).ToArray();
        var oldByRange = historicalParagraphs
            .ToDictionary(element => (element.SourceStart, element.SourceLength));
        var used = new HashSet<string>(current.ReservedPointers, StringComparer.Ordinal);
        foreach (var old in historicalParagraphs)
            used.Add(old.Pointer.Value);
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        var remapped = current.Elements.Select(element =>
        {
            if (element.Kind != MarkdownElementKind.Paragraph)
                return element;
            if (oldByRange.TryGetValue((element.SourceStart, element.SourceLength), out var old))
            {
                mapped.Add(old.Pointer.Value);
                return element with { Pointer = old.Pointer };
            }
            var section = element.SectionPointer.Value;
            var prefix = section == "document" ? "p" : section + ".p";
            var ordinal = 1;
            while (used.Contains(prefix + ordinal.ToString(CultureInfo.InvariantCulture)))
                ordinal++;
            var pointer = new SemanticPointer(prefix + ordinal.ToString(CultureInfo.InvariantCulture));
            used.Add(pointer.Value);
            return element with { Pointer = pointer };
        }).ToArray();
        var reserved = current.ReservedPointers.ToHashSet(StringComparer.Ordinal);
        foreach (var old in historicalParagraphs)
            if (!mapped.Contains(old.Pointer.Value))
                reserved.Add(old.Pointer.Value);
        return new MarkdownParseResult(remapped, reserved);
    }

    private static MarkdownParseResult ParseWithPipeline(
        MarkdownSourceDocument document, MarkdownPipeline pipeline)
    {
        var source = document.Markdown;
        var documentPointer = new SemanticPointer("document");
        var elements = new List<MarkdownElement>
        {
            new(document.RelativePath, documentPointer, MarkdownElementKind.Document,
                "", 1, 1, 0, null, documentPointer)
        };
        var syntax = Markdig.Markdown.Parse(source, pipeline);
        var headingStack = new List<HeadingContext>();
        var paragraphCounts = new Dictionary<string, int>();
        var codeCounts = new Dictionary<string, int>();
        var listCounts = new Dictionary<string, int>();
        var quoteCounts = new Dictionary<string, int>();
        var tableCounts = new Dictionary<string, int>();
        var itemPointers = new Dictionary<ListItemBlock, SemanticPointer>();
        var listDetails = new Dictionary<string, MarkdownSourceMapBuilder.ListDetail>(StringComparer.Ordinal);
        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        var rootParagraph = 0;
        var rootCode = 0;
        var rootList = 0;
        var rootQuote = 0;
        var rootTable = 0;
        var rootSectionCount = 0;
        var currentSection = documentPointer;
        string? headingPath = null;

        foreach (var block in syntax.Descendants().OfType<Block>())
        {
            var isSuppressed = HasAncestor<QuoteBlock>(block);
            var listAncestor = GetAncestor<ListItemBlock>(block);
            if (block is HeadingBlock && (isSuppressed || listAncestor is not null))
                continue;

            if (block is ListItemBlock item)
            {
                if (isSuppressed) continue;
                var parentItem = GetAncestor<ListItemBlock>(item);
                SemanticPointer pointer;
                string? parentPointer = null;
                int depth = 0;
                if (parentItem is not null)
                {
                    if (!itemPointers.TryGetValue(parentItem, out var parent))
                        throw new InvalidOperationException("List item ancestor was not parsed.");
                    parentPointer = parent.Value;
                    depth = listDetails[parent.Value].Depth + 1;
                    listCounts[parent.Value] = listCounts.GetValueOrDefault(parent.Value) + 1;
                    pointer = new SemanticPointer($"{parent.Value}.li{listCounts[parent.Value]}");
                }
                else
                {
                    pointer = NextPointer(currentSection, listCounts, ref rootList, "li");
                }

                itemPointers.Add(item, pointer);
                var range = MarkdownSourceMapBuilder.GetPhysicalRange(item, source);
                var line = source.AsSpan(range.Start, range.Length);
                var firstLineEnd = line.IndexOfAny('\r', '\n');
                var firstLine = (firstLineEnd < 0 ? line : line[..firstLineEnd]).ToString();
                var hasMarker = MarkdownListMarker.TryParse(firstLine, out var marker);
                var indent = hasMarker ? marker.Indentation.Length : -1;
                var style = hasMarker ? marker.MarkerStyle : null;
                var container = item.Parent as ListBlock;
                var containerId = container is null ? null
                    : container.Span.Start.ToString(CultureInfo.InvariantCulture);
                listDetails.Add(pointer.Value, new MarkdownSourceMapBuilder.ListDetail(
                    range, parentPointer, containerId, depth, indent, style));
                elements.Add(new MarkdownElement(
                    document.RelativePath, pointer, MarkdownElementKind.ListItem,
                    "", item.Line + 1, item.Line + CountLineBreaks(source.AsSpan(range.Start, range.Length)),
                    0, headingPath, currentSection, range.Start, range.Length));
                continue;
            }

            if (block is QuoteBlock quote)
            {
                if (isSuppressed || listAncestor is not null) continue;
                var pointer = NextPointer(currentSection, quoteCounts, ref rootQuote, "q");
                var range = MarkdownSourceMapBuilder.GetPhysicalRange(quote, source);
                var text = source.Substring(range.Start, range.Length);
                var map = MarkdownSourceMapBuilder.CreateAtomicMap(source, range);
                elements.Add(new MarkdownElement(
                    document.RelativePath, pointer, MarkdownElementKind.BlockQuote,
                    text, quote.Line + 1, quote.Line + CountLineBreaks(text),
                    0, headingPath, currentSection, range.Start, range.Length,
                    SourceMap: map));
                continue;
            }

            if (block is Table table)
            {
                if (isSuppressed || listAncestor is not null) continue;
                var pointer = NextPointer(currentSection, tableCounts, ref rootTable, "t");
                var range = MarkdownSourceMapBuilder.GetPhysicalRange(table, source);
                var text = source.Substring(range.Start, range.Length);
                elements.Add(new MarkdownElement(document.RelativePath, pointer,
                    MarkdownElementKind.Table, text, table.Line + 1,
                    table.Line + CountLineBreaks(text), 0, headingPath, currentSection,
                    range.Start, range.Length));
                continue;
            }

            // A table is atomic. Inline paragraph-like descendants are not independent elements.
            if (GetAncestor<Table>(block) is not null) continue;
            if (block is not (HeadingBlock or ParagraphBlock or CodeBlock or YamlFrontMatterBlock))
                continue;

            // Preserve old virtual counters, including the old duplicate-span exception.
            if (block is ParagraphBlock p && p.Parent is QuoteBlock && p.Span == p.Parent.Span)
                continue;

            var isCode = block is CodeBlock;
            SemanticPointer? virtualPointer = null;
            if (block is ParagraphBlock || isCode)
                virtualPointer = isCode
                    ? NextPointer(currentSection, codeCounts, ref rootCode, "code")
                    : NextPointer(currentSection, paragraphCounts, ref rootParagraph, "p");

            if (isSuppressed || listAncestor is not null)
            {
                if (virtualPointer is not null) suppressed.Add(virtualPointer.Value);
                continue;
            }

            var (start, length) = GetSpan(block, source.Length);
            if (length <= 0) continue;
            var textBlock = source.Substring(start, length);
            var startLine = block.Line + 1;
            var endLine = block.Line + CountLineBreaks(textBlock);

            if (block is YamlFrontMatterBlock)
            {
                var front = new SemanticPointer("frontmatter");
                elements.Add(new MarkdownElement(document.RelativePath, front,
                    MarkdownElementKind.FrontMatter, textBlock, startLine, endLine,
                    0, null, front, start, length));
                continue;
            }

            if (block is HeadingBlock heading)
            {
                while (headingStack.Count > 0 && headingStack[^1].Level >= heading.Level)
                    headingStack.RemoveAt(headingStack.Count - 1);
                var ordinal = headingStack.Count == 0
                    ? ++rootSectionCount : ++headingStack[^1].ChildCount;
                var pointerValue = headingStack.Count == 0
                    ? ordinal.ToString(CultureInfo.InvariantCulture)
                    : $"{headingStack[^1].Pointer.Value}.{ordinal}";
                var section = new SemanticPointer(pointerValue);
                var title = ExtractHeadingTitle(textBlock);
                headingStack.Add(new HeadingContext(heading.Level, section, title));
                currentSection = section;
                headingPath = string.Join(" > ", headingStack.Select(x => x.Title));
                elements.Add(new MarkdownElement(document.RelativePath, section,
                    MarkdownElementKind.Heading, textBlock, startLine, endLine,
                    heading.Level, headingPath, section, start, length));
                continue;
            }

            elements.Add(new MarkdownElement(document.RelativePath, virtualPointer!,
                isCode ? MarkdownElementKind.CodeBlock : MarkdownElementKind.Paragraph,
                textBlock, startLine, endLine, 0, headingPath, currentSection,
                start, length));
        }

        var fullMaps = MarkdownSourceMapBuilder.Build(source, listDetails);

        var prepared = elements.Select(element =>
        {
            if (element.Kind == MarkdownElementKind.Document)
                return element;
            if (element.Kind != MarkdownElementKind.ListItem)
                return element;
            var map = fullMaps[element.Pointer.Value];
            var ownText = string.Concat(map.OwnSegments.Select(range =>
                source.Substring(range.Start, range.Length)));
            return element with
            {
                SourceMap = map,
                Text = ownText,
                SourceLength = map.SubtreeRange.Length,
                EndLine = element.StartLine + CountLineBreaks(
                    source.AsSpan(map.SubtreeRange.Start, map.SubtreeRange.Length))
            };
        }).OrderBy(element => element.SourceStart)
          .ThenBy(element => element.Kind == MarkdownElementKind.Document ? 0 : 1)
          .ToList();

        return new MarkdownParseResult(SemanticElementHashing.Attach(source, prepared), suppressed);
    }

    private static T? GetAncestor<T>(Block block) where T : Block
    {
        var parent = block.Parent;
        while (parent is not null)
        {
            if (parent is T result) return result;
            parent = parent.Parent;
        }
        return null;
    }

    private static bool HasAncestor<T>(Block block) where T : Block
        => GetAncestor<T>(block) is not null;

    private static (int Start, int Length) GetSpan(Block block, int sourceLength)
    {
        var start = Math.Clamp(block.Span.Start, 0, sourceLength);
        var end = Math.Clamp(block.Span.End, start - 1, sourceLength - 1);
        return (start, end >= start ? end - start + 1 : 0);
    }

    private static int CountLineBreaks(ReadOnlySpan<char> value)
    {
        var result = 0;
        foreach (var character in value) if (character == '\n') result++;
        return result;
    }

    private static string ExtractHeadingTitle(string source)
    {
        var firstLine = source.Split(['\r', '\n'], 2)[0];
        var match = AtxHeadingRegex().Match(firstLine);
        return match.Success
            ? ClosingHashesRegex().Replace(match.Groups[1].Value.Trim(), "").Trim()
            : firstLine.Trim();
    }

    private static SemanticPointer NextPointer(
        SemanticPointer section, Dictionary<string, int> counts,
        ref int rootCount, string prefix)
    {
        if (section.Value == "document")
            return new SemanticPointer($"{prefix}{++rootCount}");
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
