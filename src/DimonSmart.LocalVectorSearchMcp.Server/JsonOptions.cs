using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;

namespace DimonSmart.LocalVectorSearchMcp.Server;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = CreateDefault();

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new SearchModeJsonConverter());
        options.Converters.Add(new ReindexScopeJsonConverter());
        return options;
    }
}
