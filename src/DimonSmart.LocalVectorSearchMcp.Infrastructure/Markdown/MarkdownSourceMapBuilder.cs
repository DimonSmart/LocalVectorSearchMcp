using System.Globalization;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using Markdig.Syntax;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

/// <summary>Projects Markdig block spans to exact, non-overlapping physical source ownership.</summary>
internal static class MarkdownSourceMapBuilder
{
    internal sealed record ListDetail(SourceRange Range, string? ParentPointer,
        string? ContainerId, int Depth, int Indent, string? MarkerStyle);

    public static IReadOnlyDictionary<string, MarkdownElementSourceMap> Build(
        string source, Dictionary<string, ListDetail> details)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (pointer, detail) in details)
        {
            if (detail.ParentPointer is null) continue;
            if (!children.TryGetValue(detail.ParentPointer, out var siblings))
                children[detail.ParentPointer] = siblings = [];
            siblings.Add(pointer);
        }

        // A Markdig item span may end before its nested list. Expand bottom-up.
        foreach (var pointer in details.Keys.OrderByDescending(key => details[key].Depth).ToArray())
        {
            var detail = details[pointer];
            if (!children.TryGetValue(pointer, out var descendants)) continue;
            var end = Math.Max(detail.Range.End,
                descendants.Max(child => details[child].Range.End));
            details[pointer] = detail with
            {
                Range = new SourceRange(detail.Range.Start, end - detail.Range.Start)
            };
        }

        var result = new Dictionary<string, MarkdownElementSourceMap>(details.Count,
            StringComparer.Ordinal);
        foreach (var (pointer, detail) in details)
        {
            if (detail.Range.Start < 0 || detail.Range.End > source.Length)
                throw new InvalidOperationException($"Invalid list source range at '{pointer}'.");
            var ranges = children.TryGetValue(pointer, out var directChildren)
                ? directChildren.Select(child => details[child].Range)
                    .OrderBy(range => range.Start).ToArray()
                : [];
            var own = GetOwnSegments(source, detail.Range, ranges);
            var deletion = ExtendToLineEnding(source, detail.Range);
            result.Add(pointer, new MarkdownElementSourceMap(
                detail.Range, detail.Range, deletion, own,
                detail.Range.Start, deletion.End, detail.ParentPointer,
                detail.ContainerId, detail.Depth, detail.Indent, detail.MarkerStyle));
        }
        return result;
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
            if (end > position && source[end - 1] == '\n')
            {
                end--;
                if (end > position && source[end - 1] == '\r') end--;
            }
            if (end > position) result.Add(new SourceRange(position, end - position));
            position = child.End;
            if (position < subtree.End && source[position] == '\r') position++;
            if (position < subtree.End && source[position] == '\n') position++;
        }
        if (position < subtree.End)
            result.Add(new SourceRange(position, subtree.End - position));
        return result;
    }

    public static MarkdownElementSourceMap CreateAtomicMap(string source, SourceRange range)
    {
        var deletion = ExtendToLineEnding(source, range);
        return new MarkdownElementSourceMap(range, range, deletion,
            [range], range.Start, deletion.End);
    }

    public static SourceRange GetPhysicalRange(Block block, string source)
    {
        var start = GetLineStart(source, Math.Clamp(block.Span.Start, 0, source.Length));
        var lastSpan = block.Descendants().OfType<Block>()
            .Select(child => child.Span.End)
            .Append(block.Span.End).Max();
        var last = Math.Clamp(lastSpan, start, Math.Max(start, source.Length - 1));
        var newline = last < source.Length ? source.IndexOf('\n', last) : -1;
        var end = newline >= 0 ? newline : source.Length;
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

    public static int GetLineStart(string source, int position)
    {
        if (position == 0) return 0;
        var previous = source.LastIndexOf('\n', position - 1);
        return previous < 0 ? 0 : previous + 1;
    }
}
