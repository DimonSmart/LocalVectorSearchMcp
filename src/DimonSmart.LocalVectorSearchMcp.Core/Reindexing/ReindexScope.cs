using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core;

[JsonConverter(typeof(ReindexScopeJsonConverter))]
public enum ReindexScope
{
    Changed,
    All
}
