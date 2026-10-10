using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

internal sealed record MarkdownTableEditPlan(
    string Source,
    IReadOnlyList<MarkdownSourceEdit> Edits,
    MarkdownTable Expected,
    IReadOnlySet<(int Row, int Column)> MarkdownCells);

/// <summary>Plans a fail-closed table mutation against the original document revision.</summary>
internal static class MarkdownTableEditor
{
    public static MarkdownTableEditPlan Plan(
        string source, MarkdownTableSource table, TableEditRequest request)
    {
        if (table.HasExtraCells)
            throw Invalid("The table contains extra physical cells beyond its declared columns; edit rejected to preserve hidden source data.");

        ValidateActionFields(request);
        var columns = table.Data.Columns.ToList();
        var rows = table.Data.Rows.Select(row => row.Cells.ToList()).ToList();
        var markdownCells = new HashSet<(int Row, int Column)>();
        var edits = new List<MarkdownSourceEdit>();
        var n = table.Data.ColumnCount;
        var pointer = table.Element.Pointer.Value;
        void Add(SourceRange span, string value) =>
            edits.Add(new MarkdownSourceEdit(span.Start, span.Length, value, pointer));

        switch (request.Action)
        {
            case TableEditAction.UpdateCells:
            {
                if (request.Updates is not { Count: > 0 })
                    throw Invalid("updates must contain at least one item.");
                var replacements = new Dictionary<(int Row, int Column), string>();
                foreach (var item in request.Updates)
                {
                    if (item.Value is null) throw Invalid("Each update must specify value.");
                    var r = ResolveRow(table.Data, item.RowIndex, item.Where);
                    var c = ResolveColumn(columns, item.Column, item.ColumnIndex);
                    if (!replacements.TryAdd((r, c), Format(item.Value, item.ValueFormat ?? TableValueFormat.Text)))
                        throw Invalid($"Cell at rowIndex={r}, columnIndex={c} is repeated in updates.");
                    if (item.ValueFormat == TableValueFormat.Markdown)
                        markdownCells.Add((r, c));
                    else rows[r][c] = item.Value;
                }
                foreach (var group in replacements.GroupBy(pair => pair.Key.Row))
                {
                    var line = table.Rows[group.Key];
                    if (group.Any(pair => pair.Key.Column >= line.Cells.Count))
                    {
                        var raw = RawCells(source, line, n);
                        foreach (var pair in group) raw[pair.Key.Column] = pair.Value;
                        Add(line.Range, RenderLine(line, raw));
                    }
                    else
                        foreach (var pair in group)
                            Add(line.Cells[pair.Key.Column].Content, pair.Value);
                }
                break;
            }
            case TableEditAction.InsertRow:
            {
                if ((request.Values is null) == (request.ValuesByColumn is null))
                    throw Invalid("Supply exactly one of values or valuesByColumn.");
                var values = new string[n];
                Array.Fill(values, "");
                if (request.Values is not null)
                {
                    if (request.Values.Count != n)
                        throw Invalid($"values count ({request.Values.Count}) must equal columnCount ({n}).");
                    for (var i = 0; i < n; i++)
                        values[i] = request.Values[i] ?? throw Invalid("values cannot contain null.");
                }
                else
                    foreach (var (name, value) in request.ValuesByColumn!)
                    {
                        var c = ResolveColumn(columns, name, null);
                        values[c] = value ?? throw Invalid($"Null value for column '{name}'.");
                    }
                var position = request.BeforeRowIndex ?? rows.Count;
                if (position < 0 || position > rows.Count
                    || request.BeforeRowIndex is not null && position == rows.Count)
                    throw Invalid("beforeRowIndex must identify an existing data row.");
                var raw = values.Select(value => Format(value, TableValueFormat.Text)).ToArray();
                var exemplar = table.Rows.Count > 0 ? table.Rows[0] : table.Header;
                var newLine = RenderLine(exemplar, raw);
                var eol = DetectEol(source);
                if (position < table.Rows.Count)
                    Add(new SourceRange(table.Rows[position].Range.Start, 0), newLine + eol);
                else
                    Add(new SourceRange(table.Element.SourceStart + table.Element.SourceLength, 0),
                        eol + newLine);
                rows.Insert(position, values.ToList());
                break;
            }
            case TableEditAction.DeleteRow:
            {
                var r = ResolveRow(table.Data, request.RowIndex, request.Where);
                var line = table.Rows[r];
                var start = line.Range.Start;
                var length = line.Range.Length + line.Ending.Length;
                if (line.Ending.Length == 0 && line.Range.End == source.Length)
                {
                    var previous = start >= 2 && source[start - 2] == '\r'
                        ? 2 : start >= 1 && source[start - 1] == '\n' ? 1 : 0;
                    start -= previous;
                    length += previous;
                }
                Add(new SourceRange(start, length), "");
                rows.RemoveAt(r);
                break;
            }
            case TableEditAction.RenameColumn:
            {
                var c = ResolveColumn(columns, request.Column, request.ColumnIndex);
                if (string.IsNullOrWhiteSpace(request.NewName))
                    throw Invalid("newName must not be blank.");
                Add(table.Header.Cells[c].Content, Format(request.NewName, TableValueFormat.Text));
                columns[c] = columns[c] with { Name = request.NewName };
                break;
            }
            case TableEditAction.SetAlignment:
            {
                var c = ResolveColumn(columns, request.Column, request.ColumnIndex);
                if (request.Alignment is null || !Enum.IsDefined(request.Alignment.Value))
                    throw Invalid("alignment must be none, left, center or right.");
                var span = table.Separator.Cells[c].Content;
                var dashes = Math.Max(3, source.AsSpan(span.Start, span.Length).Count('-'));
                Add(span, AlignmentDashes(request.Alignment.Value, dashes));
                columns[c] = columns[c] with { Alignment = AlignmentName(request.Alignment.Value) };
                break;
            }
            case TableEditAction.InsertColumn:
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    throw Invalid("name must not be blank.");
                var c = request.BeforeColumnIndex is not null || request.BeforeColumn is not null
                    ? ResolveColumn(columns, request.BeforeColumn, request.BeforeColumnIndex)
                    : n;
                var alignment = request.Alignment ?? TableAlignment.None;
                if (!Enum.IsDefined(alignment)) throw Invalid("Invalid alignment.");
                var value = request.DefaultValue ?? "";
                var headerRaw = RawCells(source, table.Header, n);
                var separatorRaw = RawCells(source, table.Separator, n);
                headerRaw.Insert(c, Format(request.Name, TableValueFormat.Text));
                separatorRaw.Insert(c, AlignmentDashes(alignment, 3));
                var rawLines = new List<string>
                {
                    RenderLine(table.Header, headerRaw),
                    RenderLine(table.Separator, separatorRaw)
                };
                foreach (var line in table.Rows)
                {
                    var cells = RawCells(source, line, n);
                    cells.Insert(c, Format(value, TableValueFormat.Text));
                    rawLines.Add(RenderLine(line, cells));
                }
                ReplaceWholeTable(table, rawLines, edits, pointer);
                columns.Insert(c, new MarkdownTableColumn(c, request.Name, AlignmentName(alignment)));
                for (var r = 0; r < rows.Count; r++) rows[r].Insert(c, value);
                break;
            }
            case TableEditAction.DeleteColumn:
            {
                var c = ResolveColumn(columns, request.Column, request.ColumnIndex);
                if (n == 1) throw Invalid("Cannot delete the last remaining table column.");
                var rawLines = new List<string>();
                var header = RawCells(source, table.Header, n);
                header.RemoveAt(c);
                rawLines.Add(RenderLine(table.Header, header));
                var separator = RawCells(source, table.Separator, n);
                separator.RemoveAt(c);
                rawLines.Add(RenderLine(table.Separator, separator));
                foreach (var line in table.Rows)
                {
                    var cells = RawCells(source, line, n);
                    cells.RemoveAt(c);
                    rawLines.Add(RenderLine(line, cells));
                }
                ReplaceWholeTable(table, rawLines, edits, pointer);
                columns.RemoveAt(c);
                foreach (var row in rows) row.RemoveAt(c);
                break;
            }
            default: throw Invalid("Unknown table action.");
        }

        columns = columns.Select((col, index) => col with { Index = index }).ToList();
        var expected = new MarkdownTable(columns.Count, rows.Count, columns,
            rows.Select((row, index) => new MarkdownTableRow(index, row)).ToArray());
        var relevant = edits.Where(edit =>
            !source.AsSpan(edit.Start, edit.Length).SequenceEqual(edit.Replacement.AsSpan()))
            .OrderBy(edit => edit.Start).ToArray();
        for (var i = 1; i < relevant.Length; i++)
            if (relevant[i].Start < relevant[i - 1].Start + relevant[i - 1].Length
                || relevant[i].Start == relevant[i - 1].Start)
                throw Invalid("Table source edits overlap.");
        var result = source;
        foreach (var edit in relevant.OrderByDescending(edit => edit.Start))
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        return new MarkdownTableEditPlan(result, relevant, expected, markdownCells);
    }

    public static void ValidateResult(
        MarkdownTableEditPlan plan, MarkdownTableSource actual)
    {
        if (actual.HasExtraCells || actual.Data.ColumnCount != plan.Expected.ColumnCount
            || actual.Data.RowCount != plan.Expected.RowCount)
            throw Invalid("The edited Markdown table has an unexpected shape.");
        for (var c = 0; c < plan.Expected.ColumnCount; c++)
        {
            var left = plan.Expected.Columns[c];
            var right = actual.Data.Columns[c];
            if (left.Name != right.Name || left.Alignment != right.Alignment)
                throw Invalid($"Column {c} changed unexpectedly after parsing.");
        }
        for (var r = 0; r < plan.Expected.RowCount; r++)
        for (var c = 0; c < plan.Expected.ColumnCount; c++)
        {
            if (plan.MarkdownCells.Contains((r, c))) continue;
            if (plan.Expected.Rows[r].Cells[c] != actual.Data.Rows[r].Cells[c])
                throw Invalid($"Cell at rowIndex={r}, columnIndex={c} did not preserve its expected visible value.");
        }
    }

    private static void ValidateActionFields(TableEditRequest request)
    {
        var allowed = request.Action switch
        {
            TableEditAction.UpdateCells => new[] { "Updates" },
            TableEditAction.InsertRow => new[] { "Values", "ValuesByColumn", "BeforeRowIndex" },
            TableEditAction.DeleteRow => new[] { "RowIndex", "Where" },
            TableEditAction.InsertColumn => new[] { "Name", "DefaultValue", "Alignment", "BeforeColumn", "BeforeColumnIndex" },
            TableEditAction.DeleteColumn => new[] { "Column", "ColumnIndex" },
            TableEditAction.RenameColumn => new[] { "Column", "ColumnIndex", "NewName" },
            TableEditAction.SetAlignment => new[] { "Column", "ColumnIndex", "Alignment" },
            _ => throw Invalid("Unknown table action.")
        };
        foreach (var property in typeof(TableEditRequest).GetProperties())
        {
            if (property.Name is "Path" or "Pointer" or "Action"
                || allowed.Contains(property.Name)) continue;
            if (property.GetValue(request) is not null)
                throw Invalid($"Field '{property.Name}' is not valid for action '{request.Action}'.");
        }
    }

    private static int ResolveRow(MarkdownTable data, int? rowIndex, TableWhere? where)
    {
        if ((rowIndex is null) == (where is null))
            throw Invalid("Select a row using exactly one of rowIndex or where.");
        if (rowIndex is not null)
        {
            if (rowIndex.Value < 0 || rowIndex.Value >= data.RowCount)
                throw Invalid($"rowIndex {rowIndex.Value} is outside the data rows.");
            return rowIndex.Value;
        }
        var column = ResolveColumn(data.Columns, where!.Column, where.ColumnIndex);
        if (where.EqualsValue is null) throw Invalid("where.equals is required.");
        var matches = data.Rows
            .Where(row => string.Equals(row.Cells[column], where.EqualsValue, StringComparison.Ordinal))
            .Select(row => row.Index).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw Invalid($"No row has value '{where.EqualsValue}' in column {column}."),
            _ => throw Conflict($"Found {matches.Length} rows with value '{where.EqualsValue}' in column {column}; use rowIndex.")
        };
    }

    private static int ResolveColumn(
        IReadOnlyList<MarkdownTableColumn> columns, string? name, int? index)
    {
        if ((name is null) == (index is null))
            throw Invalid("Select a column using exactly one of column or columnIndex.");
        if (index is not null)
        {
            if (index < 0 || index >= columns.Count)
                throw Invalid($"columnIndex {index} is outside the table columns.");
            return index.Value;
        }
        if (name!.Length == 0)
            throw Invalid("An empty column name must be selected using columnIndex.");
        var matches = columns.Where(column =>
            string.Equals(column.Name, name, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0].Index,
            0 => throw Invalid($"Column '{name}' was not found."),
            _ => throw Conflict($"Column '{name}' has multiple matches; use columnIndex.")
        };
    }

    private static List<string> RawCells(string source, MarkdownTableSource.Line line, int count)
    {
        var cells = line.Cells.Select(cell =>
            source.Substring(cell.Content.Start, cell.Content.Length)).ToList();
        while (cells.Count < count) cells.Add("");
        return cells;
    }

    private static void ReplaceWholeTable(
        MarkdownTableSource table, IReadOnlyList<string> lines,
        List<MarkdownSourceEdit> edits, string pointer)
    {
        var original = new[] { table.Header, table.Separator }.Concat(table.Rows).ToArray();
        var text = string.Concat(lines.Select((line, i) => line + original[i].Ending));
        edits.Add(new MarkdownSourceEdit(table.Element.SourceStart,
            table.Element.SourceLength, text, pointer));
    }

    private static string RenderLine(MarkdownTableSource.Line original, IReadOnlyList<string> cells)
        => (original.LeadingPipe ? "| " : "") + string.Join(" | ", cells) +
            (original.TrailingPipe ? " |" : "");

    private static string DetectEol(string source)
        => source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string AlignmentName(TableAlignment value)
        => value.ToString().ToLowerInvariant();

    private static string AlignmentDashes(TableAlignment alignment, int count)
    {
        var dashes = new string('-', count);
        return alignment switch
        {
            TableAlignment.None => dashes,
            TableAlignment.Left => ":" + dashes,
            TableAlignment.Center => ":" + dashes + ":",
            TableAlignment.Right => dashes + ":",
            _ => throw Invalid("Invalid alignment.")
        };
    }

    private static string Format(string value, TableValueFormat format)
    {
        if (value.Any(character =>
            character is '\r' or '\n' or '\0' || char.IsControl(character)))
            throw Invalid("A table cell cannot contain line breaks, NUL or control characters.");
        if (format == TableValueFormat.Markdown)
        {
            if (HasUnescapedPipe(value))
                throw Invalid("Inline Markdown values must escape pipe characters as \\|.");
            return value;
        }
        if (format != TableValueFormat.Text)
            throw Invalid("valueFormat must be text or markdown.");
        var result = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': result.Append("&amp;"); break;
                case '<': result.Append("&lt;"); break;
                case '>': result.Append("&gt;"); break;
                case '\\': case '|': case '*': case '_': case (char)96: case '[':
                case ']': case '!': case '~': case '#':
                    result.Append('\\').Append(ch); break;
                default: result.Append(ch); break;
            }
        }
        return result.ToString();
    }

    private static bool HasUnescapedPipe(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length) { i++; continue; }
            if (value[i] == '|') return true;
        }
        return false;
    }

    private static WorkspaceMutationException Invalid(string message) => new(message);
    private static SemanticAnchorConflictException Conflict(string message)
        => new(SemanticAnchorConflictReason.AmbiguousSemanticPointer, message);
}
