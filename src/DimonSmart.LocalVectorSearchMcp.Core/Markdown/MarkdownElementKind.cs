using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

[JsonConverter(typeof(JsonStringEnumConverter<MarkdownElementKind>))]
public enum MarkdownElementKind
{
    [JsonStringEnumMemberName("document")]
    Document,

    [JsonStringEnumMemberName("front_matter")]
    FrontMatter,

    [JsonStringEnumMemberName("heading")]
    Heading,

    [JsonStringEnumMemberName("paragraph")]
    Paragraph,

    [JsonStringEnumMemberName("code_block")]
    CodeBlock,

    [JsonStringEnumMemberName("list_item")]
    ListItem,

    [JsonStringEnumMemberName("table")]
    Table,

    [JsonStringEnumMemberName("block_quote")]
    BlockQuote
}
