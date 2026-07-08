using System.Text.Json;
using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed class ReindexScopeJsonConverter : JsonConverter<ReindexScope>
{
    public override ReindexScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !ReindexScopeExtensions.TryParseWireValue(reader.GetString(), out var scope))
        {
            throw new JsonException("Reindex scope must be changed or all.");
        }

        return scope;
    }

    public override void Write(Utf8JsonWriter writer, ReindexScope value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToWireValue());
}
