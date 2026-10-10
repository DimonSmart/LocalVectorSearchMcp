using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

[JsonConverter(typeof(Serialization.StrictJsonStringEnumConverter<SearchMode>))]
public enum SearchMode
{
    [JsonStringEnumMemberName("semantic")]
    Semantic,

    [JsonStringEnumMemberName("lexical")]
    Lexical,

    [JsonStringEnumMemberName("hybrid")]
    Hybrid
}
