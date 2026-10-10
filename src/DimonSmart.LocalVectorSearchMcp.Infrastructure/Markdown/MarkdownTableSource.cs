using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

/// <summary>
/// Projects one Markdig Table AST onto original physical row/cell spans.
/// The small lexer only identifies unescaped GFM pipes; the AST owns semantic interpretation.
/// </summary>
internal sealed class MarkdownTableSource
{
    internal sealed record Cell(SourceRange Content, SourceRange Physical);
    internal sealed record Line(SourceRange Range, string Ending, IReadOnlyList<Cell> Cells, bool LeadingPipe, bool TrailingPipe);

    public MarkdownElement Element { get; }
    public MarkdownTable Data { get; }
    public Line Header { get; }
    public Line Separator { get; }
    public IReadOnlyList<Line> Rows { get; }
    public bool HasExtraCells { get; }

    private MarkdownTableSource(
        MarkdownElement element, MarkdownTable data,
        Line header, Line separator, IReadOnlyList<Line> rows, bool extras)
    {
        Element = element;
        Data = data;
        Header = header;
        Separator = separator;
        Rows = rows;
        HasExtraCells = extras;
    }

    public static MarkdownTableSource Read(string source, MarkdownElement element)
    {
        if (element.Kind != MarkdownElementKind.Table)
            throw new WorkspaceMutationException("The target pointer must refer to a Markdown table.");

        var syntax = Markdig.Markdown.Parse(source, MarkdownPipelines.Tables);
        var ast = syntax.Descendants().OfType<Table>().SingleOrDefault(table =>
            MarkdownSourceMapBuilder.GetPhysicalRange(table, source).Start == element.SourceStart);
        if (ast is null || ast.Parent is not MarkdownDocument)
            throw new WorkspaceMutationException("A standalone GFM pipe table was not found at the selected pointer.");

        var tableRows = ast.OfType<TableRow>().ToArray();
        if (tableRows.Length == 0 || !tableRows[0].IsHeader)
            throw new WorkspaceMutationException("A GFM header row is required.");
        var columnCount = tableRows[0].OfType<TableCell>().Count();
        if (columnCount == 0)
            throw new WorkspaceMutationException("The table has no declared columns.");

        var physical = ReadLines(source, element.SourceStart, element.SourceLength);
        if (physical.Count != tableRows.Length + 1 || physical.Count < 2)
            throw new WorkspaceMutationException("Table row spans cannot be mapped unambiguously.");
        var header = physical[0];
        var separator = physical[1];
        if (header.Cells.Count != columnCount || separator.Cells.Count != columnCount)
            throw new WorkspaceMutationException("Header or delimiter cell count does not match the GFM AST.");

        var columns = Enumerable.Range(0, columnCount)
            .Select(i => new MarkdownTableColumn(i,
                Display(tableRows[0].OfType<TableCell>().ElementAt(i)),
                Alignment(source.AsSpan(separator.Cells[i].Content.Start, separator.Cells[i].Content.Length))))
            .ToArray();
        var dataRows = tableRows.Skip(1).Select((row, index) =>
        {
            var cells = row.OfType<TableCell>().Select(Display).ToList();
            while (cells.Count < columnCount) cells.Add("");
            return new MarkdownTableRow(index, cells.Take(columnCount).ToArray());
        }).ToArray();
        var body = physical.Skip(2).ToArray();
        var extras = body.Any(row => row.Cells.Count > columnCount);
        return new MarkdownTableSource(element,
            new MarkdownTable(columnCount, dataRows.Length, columns, dataRows),
            header, separator, body, extras);
    }

    private static string Display(TableCell cell)
    {
        var text = new StringBuilder();
        foreach (var leaf in cell.Descendants().OfType<LeafBlock>())
            if (leaf.Inline is not null) AppendInlines(leaf.Inline.FirstChild, text);
        return text.ToString();
    }

    private static void AppendInlines(Inline? inline, StringBuilder text)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case HtmlEntityInline entity:
                    text.Append(entity.Transcoded.ToString());
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
                case ContainerInline container:
                    AppendInlines(container.FirstChild, text);
                    break;
                default:
                    // Deterministic fallback for unfamiliar inline extensions.
                    // No arbitrary HTML rendering or tag stripping.
                    text.Append(current.ToString());
                    break;
            }
        }
    }

    private static TableAlignment Alignment(ReadOnlySpan<char> separator)
    {
        var text = separator.Trim();
        var left = text.StartsWith(":", StringComparison.Ordinal);
        var right = text.EndsWith(":", StringComparison.Ordinal);
        return (left, right) switch
        {
            (true, true) => TableAlignment.Center,
            (true, false) => TableAlignment.Left,
            (false, true) => TableAlignment.Right,
            _ => TableAlignment.None
        };
    }

    private static IReadOnlyList<Line> ReadLines(string source, int start, int length)
    {
        var result = new List<Line>();
        var end = start + length;
        for (var position = start; position < end;)
        {
            var lineEnd = source.IndexOf('\n', position, end - position);
            var eolEnd = lineEnd >= 0 ? lineEnd + 1 : end;
            var physicalEnd = lineEnd >= 0 && lineEnd > position && source[lineEnd - 1] == '\r'
                ? lineEnd - 1 : lineEnd >= 0 ? lineEnd : end;
            result.Add(ParseLine(source, position, physicalEnd,
                source[physicalEnd..eolEnd]));
            position = eolEnd;
        }
        return result;
    }

    private static Line ParseLine(string source, int start, int end, string ending)
    {
        var first = start;
        while (first < end && source[first] is ' ' or '\t') first++;
        var last = end;
        while (last > first && source[last - 1] is ' ' or '\t') last--;
        var leading = first < last && source[first] == '|';
        var trailing = last > first && source[last - 1] == '|';
        var bodyStart = leading ? first + 1 : start;
        var bodyEnd = trailing ? last - 1 : end;
        var boundaries = new List<int> { bodyStart };
        for (var i = bodyStart; i < bodyEnd; i++)
        {
            if (source[i] == '\\' && i + 1 < bodyEnd) { i++; continue; }
            if (source[i] == '|') boundaries.Add(i + 1);
        }
        boundaries.Add(bodyEnd + 1);
        var cells = new List<Cell>();
        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var physicalStart = boundaries[i];
            var physicalEnd = boundaries[i + 1] - 1;
            if (physicalEnd < physicalStart) physicalEnd = physicalStart;
            var valueStart = physicalStart;
            while (valueStart < physicalEnd && source[valueStart] is ' ' or '\t') valueStart++;
            var valueEnd = physicalEnd;
            while (valueEnd > valueStart && source[valueEnd - 1] is ' ' or '\t') valueEnd--;
            cells.Add(new Cell(
                new SourceRange(valueStart, valueEnd - valueStart),
                new SourceRange(physicalStart, physicalEnd - physicalStart)));
        }
        return new Line(new SourceRange(start, end - start), ending, cells, leading, trailing);
    }
}
