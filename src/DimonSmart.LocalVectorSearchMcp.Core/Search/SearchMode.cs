using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

[JsonConverter(typeof(SearchModeJsonConverter))]
public enum SearchMode
{
    Semantic,
    Lexical,
    Hybrid
}
