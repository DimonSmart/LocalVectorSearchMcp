using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core;

[JsonConverter(typeof(SearchModeJsonConverter))]
public enum SearchMode
{
    Semantic,
    Lexical,
    Hybrid
}
