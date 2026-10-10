using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

[JsonConverter(typeof(Serialization.StrictJsonStringEnumConverter<SearchMode>))]
public enum SearchMode
{
    Semantic,
    Lexical,
    Hybrid
}
