using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[McpServerToolType]
public sealed class FileTransferDiagnosticMcpTools(IHttpClientFactory httpClientFactory)
{
    private const long MaxDownloadBytes = 25L * 1024 * 1024;
    private const string TestPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [McpServerTool(
        Name = "debug_receive_file",
        UseStructuredContent = true,
        OutputSchemaType = typeof(FileTransferDiagnosticResponse))]
    [McpMeta("openai/fileParams", JsonValue = """["file"]""")]
    [Description(
        "Diagnostic-only tool for testing ChatGPT file handoff through MCP. " +
        "Accepts an OpenAI file parameter, downloads its temporary HTTPS URL without persisting it, " +
        "and returns size, SHA-256 and MIME diagnostics.")]
    public async Task<CallToolResult> ReceiveFileAsync(
        [Description("File supplied by ChatGPT through the OpenAI file-parameter mechanism.")]
        OpenAiFileParameter file,
        CancellationToken cancellationToken)
    {
        if (file.InputError is not null)
        {
            return Error($"Invalid OpenAI file parameter: {file.InputError}");
        }

        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
        {
            return Error("The file parameter does not contain download_url.");
        }

        if (string.IsNullOrWhiteSpace(file.FileId))
        {
            return Error("The file parameter does not contain file_id.");
        }

        if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Error("download_url must be an absolute HTTPS URL.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var response = await client.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Error(
                    $"Downloading the supplied file failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }

            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
            {
                return Error($"The supplied file exceeds the {MaxDownloadBytes} byte diagnostic limit.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var diagnostic = await ReadDiagnosticAsync(
                file,
                response.Content.Headers.ContentType?.MediaType,
                stream,
                cancellationToken);

            var json = JsonSerializer.Serialize(diagnostic, JsonOptions.Default);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = json }],
                StructuredContent = JsonSerializer.SerializeToElement(diagnostic, JsonOptions.Default)
            };
        }
        catch (HttpRequestException exception)
        {
            return Error($"Downloading the supplied file failed: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            return Error(exception.Message);
        }
    }

    [McpServerTool(Name = "debug_return_test_image")]
    [Description(
        "Diagnostic-only tool that returns a tiny valid PNG as MCP image content. " +
        "Use it to verify MCP-to-ChatGPT image transfer independently of file upload.")]
    public static ImageContentBlock ReturnTestImage()
        => ImageContentBlock.FromBytes(Convert.FromBase64String(TestPngBase64), "image/png");

    private static async Task<FileTransferDiagnosticResponse> ReadDiagnosticAsync(
        OpenAiFileParameter file,
        string? responseMimeType,
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        var prefix = new byte[16];
        var prefixLength = 0;
        long totalBytes = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > MaxDownloadBytes)
            {
                throw new InvalidDataException(
                    $"The supplied file exceeds the {MaxDownloadBytes} byte diagnostic limit.");
            }

            hash.AppendData(buffer.AsSpan(0, read));

            if (prefixLength < prefix.Length)
            {
                var copyLength = Math.Min(prefix.Length - prefixLength, read);
                buffer.AsSpan(0, copyLength).CopyTo(prefix.AsSpan(prefixLength));
                prefixLength += copyLength;
            }
        }

        return new FileTransferDiagnosticResponse(
            file.FileId,
            file.FileName,
            file.MimeType,
            responseMimeType,
            totalBytes,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(prefix.AsSpan(0, prefixLength)).ToLowerInvariant());
    }

    private static CallToolResult Error(string message)
        => new()
        {
            Content = [new TextContentBlock { Text = message }],
            IsError = true
        };
}

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

internal sealed class OpenAiFileParameterJsonConverter : JsonConverter<OpenAiFileParameter>
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
            return Invalid($"expected a JSON object, received {root.ValueKind}.");
        }

        var downloadUrl = ReadString(root, "download_url", out var downloadUrlError);
        var fileId = ReadString(root, "file_id", out var fileIdError);
        var mimeType = ReadString(root, "mime_type", out var mimeTypeError);
        var fileName = ReadString(root, "file_name", out var fileNameError);

        return new OpenAiFileParameter(downloadUrl, fileId, mimeType, fileName)
        {
            InputError = downloadUrlError ?? fileIdError ?? mimeTypeError ?? fileNameError
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
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            error = null;
            return "";
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"property '{propertyName}' must be a JSON string, received {property.ValueKind}.";
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

public sealed record FileTransferDiagnosticResponse(
    string FileId,
    string FileName,
    string DeclaredMimeType,
    string? ResponseMimeType,
    long Bytes,
    string Sha256,
    string FirstBytesHex);
