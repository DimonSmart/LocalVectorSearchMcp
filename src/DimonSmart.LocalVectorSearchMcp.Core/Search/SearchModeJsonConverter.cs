using System.Text.Json;
using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed class SearchModeJsonConverter : JsonConverter<SearchMode>
{
    public override SearchMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !SearchModeExtensions.TryParseWireValue(reader.GetString(), out var mode))
        {
            throw new JsonException("Search mode must be semantic, lexical or hybrid.");
        }

        return mode;
    }

    public override void Write(Utf8JsonWriter writer, SearchMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToWireValue());
}
