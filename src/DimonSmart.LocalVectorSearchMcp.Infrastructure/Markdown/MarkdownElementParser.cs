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
        var source = document.Markdown;
        var documentPointer = new SemanticPointer("document");
        var elements = new List<MarkdownElement>
        {
            new(document.RelativePath, documentPointer, MarkdownElementKind.Document,
                "", 1, 1, 0, null, documentPointer)
        };
        var syntax = Markdig.Markdown.Parse(source, Pipeline);
        var headingStack = new List<HeadingContext>();
        var paragraphCounts = new Dictionary<string, int>();
        var codeCounts = new Dictionary<string, int>();
        var listCounts = new Dictionary<string, int>();
        var quoteCounts = new Dictionary<string, int>();
        var itemPointers = new Dictionary<ListItemBlock, SemanticPointer>();
        var listDetails = new Dictionary<string, ListDetail>(StringComparer.Ordinal);
        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        var rootParagraph = 0;
        var rootCode = 0;
        var rootList = 0;
        var rootQuote = 0;
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
                var range = GetPhysicalRange(item, source);
                var line = source.AsSpan(range.Start, range.Length);
                var firstLineEnd = line.IndexOfAny('\r', '\n');
                var firstLine = (firstLineEnd < 0 ? line : line[..firstLineEnd]).ToString();
                var markerMatch = ListMarkerRegex().Match(firstLine);
                var indent = markerMatch.Success ? markerMatch.Groups["indent"].Length : -1;
                var rawMarker = markerMatch.Success ? markerMatch.Groups["marker"].Value : "";
                var style = rawMarker.Length == 0 ? null :
                    char.IsDigit(rawMarker[0]) ? "ordered:" + rawMarker[^1] : rawMarker;
                var container = item.Parent as ListBlock;
                var containerId = container is null ? null : container.Span.Start.ToString(CultureInfo.InvariantCulture);
                listDetails.Add(pointer.Value, new ListDetail(
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
                var range = GetPhysicalRange(quote, source);
                var text = source.Substring(range.Start, range.Length);
                var map = CreateAtomicMap(source, range);
                elements.Add(new MarkdownElement(
                    document.RelativePath, pointer, MarkdownElementKind.BlockQuote,
                    text, quote.Line + 1, quote.Line + CountLineBreaks(text),
                    0, headingPath, currentSection, range.Start, range.Length,
                    SourceMap: map));
                continue;
            }

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

        var fullMaps = new Dictionary<string, MarkdownElementSourceMap>(StringComparer.Ordinal);
        foreach (var (pointer, detail) in listDetails)
        {
            var children = listDetails.Values
                .Where(x => x.ParentPointer == pointer)
                .Select(x => x.Range)
                .OrderBy(x => x.Start).ToArray();
            var own = GetOwnSegments(source, detail.Range, children);
            fullMaps.Add(pointer, new MarkdownElementSourceMap(
                detail.Range, detail.Range, ExtendToLineEnding(source, detail.Range),
                own, detail.Range.Start, ExtendToLineEnding(source, detail.Range).End,
                detail.ParentPointer, detail.ContainerId, detail.Depth,
                detail.Indent, detail.MarkerStyle));
        }

        var prepared = elements.Select(element =>
        {
            if (element.Kind == MarkdownElementKind.Document)
                return element with { ReservedPointers = suppressed };
            if (element.Kind != MarkdownElementKind.ListItem)
                return element;
            var map = fullMaps[element.Pointer.Value];
            var ownText = string.Concat(map.OwnSegments.Select(range =>
                source.Substring(range.Start, range.Length)));
            return element with { SourceMap = map, Text = ownText };
        }).OrderBy(element => element.SourceStart)
          .ThenBy(element => element.Kind == MarkdownElementKind.Document ? 0 : 1)
          .ToList();

        return SemanticElementHashing.Attach(source, prepared);
    }

    private static IReadOnlyList<SourceRange> GetOwnSegments(
        string source, SourceRange subtree, IReadOnlyList<SourceRange> childRanges)
    {
        var result = new List<SourceRange>();
        var position = subtree.Start;
        foreach (var child in childRanges)
        {
            if (child.Start < position || child.End > subtree.End)
                throw new InvalidOperationException("Overlapping Markdown list item spans.");
            var end = child.Start;
            // The newline joining the parent to the child is structural, not owned text.
            if (end > position && source[end - 1] == '\n')
            {
                end--;
                if (end > position && source[end - 1] == '\r') end--;
            }
            if (end > position) result.Add(new SourceRange(position, end - position));
            position = child.End;
            // A child boundary owns its terminal physical newline only for editing.
            if (position < subtree.End && source[position] == '\r') position++;
            if (position < subtree.End && source[position] == '\n') position++;
        }
        if (position < subtree.End)
            result.Add(new SourceRange(position, subtree.End - position));
        return result;
    }

    private static MarkdownElementSourceMap CreateAtomicMap(string source, SourceRange range)
        => new(range, range, ExtendToLineEnding(source, range),
            [range], range.Start, ExtendToLineEnding(source, range).End);

    private static SourceRange GetPhysicalRange(Block block, string source)
    {
        var start = GetLineStart(source, Math.Clamp(block.Span.Start, 0, source.Length));
        var lastSpan = block.Descendants().OfType<Block>()
            .Select(child => child.Span.End)
            .Append(block.Span.End)
            .Max();
        var last = Math.Clamp(lastSpan, start, Math.Max(start, source.Length - 1));
        var end = last < source.Length
            ? source.IndexOf('\n', last) is var newline && newline >= 0 ? newline : source.Length
            : source.Length;
        if (end > start && source[end - 1] == '\r') end--;
        return new SourceRange(start, end - start);
    }

    private static SourceRange ExtendToLineEnding(string source, SourceRange range)
    {
        var end = range.End;
        if (end < source.Length && source[end] == '\r') end++;
        if (end < source.Length && source[end] == '\n') end++;
        return new SourceRange(range.Start, end - range.Start);
    }

    private static int GetLineStart(string source, int position)
    {
        if (position == 0) return 0;
        var previous = source.LastIndexOf('\n', position - 1);
        return previous < 0 ? 0 : previous + 1;
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

    private sealed record ListDetail(SourceRange Range, string? ParentPointer,
        string? ContainerId, int Depth, int Indent, string? MarkerStyle);

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

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-+*]|[0-9]+[.)])(?=[ \t]|$)")]
    private static partial Regex ListMarkerRegex();
}
