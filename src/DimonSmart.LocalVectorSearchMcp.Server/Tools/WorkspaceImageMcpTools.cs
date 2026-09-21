using System.ComponentModel;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceImageMcpTools(
    IWorkspaceImageService images)
{
    [McpServerTool(
        Name = "kb_save_image",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageSaveResponse))]
    [McpMeta("openai/fileParams", JsonValue = """["file"]""")]
    [Description(
        "Saves a PNG, JPEG, WebP, or GIF supplied through the OpenAI file parameter into the workspace images/ directory. " +
        "Requires knowledgeBase.allowWrites=true and never overwrites an existing image.")]
    public async Task<CallToolResult> SaveImageAsync(
        [Description(
            "Image supplied by ChatGPT through the OpenAI file-parameter mechanism.")]
        OpenAiFileParameter file,
        CancellationToken cancellationToken,
        [Description(
            "Optional explicit portable basename for the saved image. Directories are not allowed.")]
        string? fileName = null,
        [Description(
            "Optional alt text used to build the returned Markdown image reference.")]
        string? altText = null)
    {
        if (file.InputError is not null)
        {
            return Error(
                $"Invalid OpenAI file parameter: {file.InputError}");
        }

        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
        {
            return Error(
                "The file parameter does not contain download_url.");
        }

        if (string.IsNullOrWhiteSpace(file.FileId))
        {
            return Error(
                "The file parameter does not contain file_id.");
        }

        try
        {
            var response = await images.SaveAsync(
                new SaveImageRequest(
                    file.DownloadUrl,
                    file.MimeType,
                    file.FileName,
                    fileName,
                    altText),
                cancellationToken);
            return Structured(response);
        }
        catch (Exception exception) when (
            exception is WorkspaceImageException
                or KnowledgeBaseAccessException)
        {
            return Error(exception.Message);
        }
    }

    [McpServerTool(
        Name = "kb_list_images",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageListResponse))]
    [Description(
        "Lists PNG, JPEG, WebP, and GIF assets recursively under workspace images/ using opaque cursor pagination.")]
    public async Task<CallToolResult> ListImagesAsync(
        CancellationToken cancellationToken,
        [Description("Opaque cursor returned by the previous page.")]
        string? cursor = null,
        [Description(
            "Page size from 1 to 200. Defaults to 50.")]
        int? pageSize = null)
    {
        try
        {
            var response = await images.ListAsync(
                cursor,
                pageSize,
                cancellationToken);
            return Structured(response);
        }
        catch (Exception exception) when (
            exception is WorkspaceImageException
                or KnowledgeBaseAccessException)
        {
            return Error(exception.Message);
        }
    }

    [McpServerTool(
        Name = "kb_load_image",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageLoadResponse))]
    [Description(
        "Loads an existing supported image under workspace images/ and returns a real MCP image content block plus structured metadata.")]
    public async Task<CallToolResult> LoadImageAsync(
        [Description(
            "Project-relative image path under images/, for example images/chapter-01.png.")]
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await images.LoadAsync(
                path,
                cancellationToken);
            var metadataJson = JsonSerializer.Serialize(
                loaded.Metadata,
                JsonOptions.Default);
            return new CallToolResult
            {
                Content =
                [
                    ImageContentBlock.FromBytes(
                        loaded.Data,
                        loaded.Metadata.MimeType),
                    new TextContentBlock
                    {
                        Text = metadataJson
                    }
                ],
                StructuredContent =
                    JsonSerializer.SerializeToElement(
                        loaded.Metadata,
                        JsonOptions.Default)
            };
        }
        catch (Exception exception) when (
            exception is WorkspaceImageException
                or KnowledgeBaseAccessException)
        {
            return Error(exception.Message);
        }
    }

    [McpServerTool(
        Name = "kb_delete_image",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageDeleteResponse))]
    [Description(
        "Deletes one supported image file under workspace images/. Requires knowledgeBase.allowWrites=true and never removes parent directories.")]
    public async Task<CallToolResult> DeleteImageAsync(
        [Description(
            "Project-relative image path under images/.")]
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await images.DeleteAsync(
                path,
                cancellationToken);
            return Structured(response);
        }
        catch (Exception exception) when (
            exception is WorkspaceImageException
                or KnowledgeBaseAccessException)
        {
            return Error(exception.Message);
        }
    }

    private static CallToolResult Structured<T>(T response)
    {
        var json = JsonSerializer.Serialize(
            response,
            JsonOptions.Default);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = json
                }
            ],
            StructuredContent = JsonSerializer.SerializeToElement(
                response,
                JsonOptions.Default)
        };
    }

    private static CallToolResult Error(string message)
        => new()
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = message
                }
            ],
            IsError = true
        };
}
