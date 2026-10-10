using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

[JsonConverter(typeof(Serialization.StrictJsonStringEnumConverter<ReindexScope>))]
public enum ReindexScope
{
    [JsonStringEnumMemberName("changed")]
    Changed,

    [JsonStringEnumMemberName("all")]
    All
}
