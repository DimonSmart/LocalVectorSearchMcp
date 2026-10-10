using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

[JsonConverter(typeof(JsonStringEnumConverter<TableEditAction>))]
public enum TableEditAction
{
    [JsonStringEnumMemberName("update_cells")] UpdateCells,
    [JsonStringEnumMemberName("insert_row")] InsertRow,
    [JsonStringEnumMemberName("delete_row")] DeleteRow,
    [JsonStringEnumMemberName("insert_column")] InsertColumn,
    [JsonStringEnumMemberName("delete_column")] DeleteColumn,
    [JsonStringEnumMemberName("rename_column")] RenameColumn,
    [JsonStringEnumMemberName("set_alignment")] SetAlignment
}

[JsonConverter(typeof(JsonStringEnumConverter<TableValueFormat>))]
public enum TableValueFormat
{
    [JsonStringEnumMemberName("text")] Text,
    [JsonStringEnumMemberName("markdown")] Markdown
}

[JsonConverter(typeof(JsonStringEnumConverter<TableAlignment>))]
public enum TableAlignment
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("left")] Left,
    [JsonStringEnumMemberName("center")] Center,
    [JsonStringEnumMemberName("right")] Right
}

public sealed record TableWhere(
    string? Column = null,
    int? ColumnIndex = null,
    [property: JsonPropertyName("equals")] string? EqualsValue = null);

public sealed record TableCellUpdate(
    string? Value = null,
    int? RowIndex = null,
    TableWhere? Where = null,
    string? Column = null,
    int? ColumnIndex = null,
    TableValueFormat? ValueFormat = null);

/// <summary>One atomic operation against a current hashed GFM table pointer.</summary>
public sealed record TableEditRequest(
    string Path,
    string Pointer,
    TableEditAction Action,
    [property: Description("Nonempty batch of cell updates, addressed relative to the original table.")]
    IReadOnlyList<TableCellUpdate>? Updates = null,
    int? RowIndex = null,
    TableWhere? Where = null,
    string? Column = null,
    int? ColumnIndex = null,
    IReadOnlyList<string>? Values = null,
    IReadOnlyDictionary<string, string>? ValuesByColumn = null,
    int? BeforeRowIndex = null,
    string? BeforeColumn = null,
    int? BeforeColumnIndex = null,
    string? Name = null,
    string? NewName = null,
    string? DefaultValue = null,
    TableAlignment? Alignment = null);
public sealed record TableEditResponse(
    string Path,
    string Pointer,
    string SourceHash,
    bool IndexSynchronized,
    string? IndexError = null);
