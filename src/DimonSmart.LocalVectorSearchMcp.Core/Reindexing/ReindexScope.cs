using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

[JsonConverter(typeof(ReindexScopeJsonConverter))]
public enum ReindexScope
{
    Changed,
    All
}
