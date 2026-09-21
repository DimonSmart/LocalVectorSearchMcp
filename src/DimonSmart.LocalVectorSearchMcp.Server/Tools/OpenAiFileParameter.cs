using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

public sealed class OpenAiFileParameter
{
    [JsonPropertyName("download_url")]
    [Required]
    public string DownloadUrl { get; init; } = "";

    [JsonPropertyName("file_id")]
    [Required]
    public string FileId { get; init; } = "";

    [JsonPropertyName("mime_type")]
    public string MimeType { get; init; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = "";
}
