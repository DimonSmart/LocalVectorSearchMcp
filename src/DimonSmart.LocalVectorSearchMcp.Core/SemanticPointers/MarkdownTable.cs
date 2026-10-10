using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed record MarkdownTableColumn(
    int Index,
    string Name,
    TableAlignment Alignment);
public sealed record MarkdownTableRow(int Index, IReadOnlyList<string> Cells);
public sealed record MarkdownTable(
    int ColumnCount,
    int RowCount,
    IReadOnlyList<MarkdownTableColumn> Columns,
    IReadOnlyList<MarkdownTableRow> Rows);
