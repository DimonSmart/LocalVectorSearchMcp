using System.Text.Json;
using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[JsonConverter(typeof(OpenAiFileParameterJsonConverter))]
public sealed record OpenAiFileParameter(
    [property: JsonPropertyName("download_url")]
    string DownloadUrl,
    [property: JsonPropertyName("file_id")]
    string FileId,
    [property: JsonPropertyName("mime_type")]
    string MimeType = "",
    [property: JsonPropertyName("file_name")]
    string FileName = "")
{
    internal string? InputError { get; init; }
}

internal sealed class OpenAiFileParameterJsonConverter :
    JsonConverter<OpenAiFileParameter>
{
    public override bool HandleNull => true;

    public override OpenAiFileParameter Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid(
                $"expected a JSON object, received {root.ValueKind}.");
        }

        var downloadUrl = ReadString(
            root,
            "download_url",
            out var downloadUrlError);
        var fileId = ReadString(
            root,
            "file_id",
            out var fileIdError);
        var mimeType = ReadString(
            root,
            "mime_type",
            out var mimeTypeError);
        var fileName = ReadString(
            root,
            "file_name",
            out var fileNameError);

        return new OpenAiFileParameter(
            downloadUrl,
            fileId,
            mimeType,
            fileName)
        {
            InputError = downloadUrlError
                         ?? fileIdError
                         ?? mimeTypeError
                         ?? fileNameError
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        OpenAiFileParameter value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("download_url", value.DownloadUrl);
        writer.WriteString("file_id", value.FileId);
        writer.WriteString("mime_type", value.MimeType);
        writer.WriteString("file_name", value.FileName);
        writer.WriteEndObject();
    }

    private static string ReadString(
        JsonElement root,
        string propertyName,
        out string? error)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null
                or JsonValueKind.Undefined)
        {
            error = null;
            return "";
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error =
                $"property '{propertyName}' must be a JSON string, received {property.ValueKind}.";
            return "";
        }

        error = null;
        return property.GetString() ?? "";
    }

    private static OpenAiFileParameter Invalid(string error)
        => new("", "")
        {
            InputError = error
        };
}
