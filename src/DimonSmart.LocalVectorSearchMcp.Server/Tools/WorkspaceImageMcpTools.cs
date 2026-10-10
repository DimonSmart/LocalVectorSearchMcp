using System.ComponentModel;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceImageMcpTools(
    IWorkspaceImageService images,
    IWorkspaceImageMoveService imageMoves)
{
    [McpServerTool(
        Name = "kb_save_image",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageSaveResponse))]
    [McpMeta("openai/fileParams", JsonValue = """["file"]""")]
    [Description(
        "Saves a PNG, JPEG, WebP, or GIF supplied through the client-supported OpenAI file-parameter mechanism inside knowledgeBase.root (by default images/). " +
        "Requires knowledgeBase.allowWrites=true and never overwrites an existing image.")]
    public async Task<CallToolResult> SaveImageAsync(
        [Description(
            "Image file reference supplied through the OpenAI file-parameter mechanism. Base64 strings and data URIs are not supported.")]
        OpenAiFileParameter file,
        CancellationToken cancellationToken,
        [Description(
            "Optional explicit portable basename for the saved image. Directories are not allowed.")]
        string? fileName = null,
        [Description(
            "Optional alt text used to build the returned Markdown image reference.")]
        string? altText = null,
        [Description("Optional exact root-relative destination including the image file name. Cannot be combined with fileName.")]
        string? targetPath = null)
    {
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
                    altText,
                    targetPath),
                cancellationToken);
            return Structured(response);
        }
        catch (Exception exception) when (
            exception is WorkspaceImageException
                or KnowledgeBaseAccessException)
        {
            return ToolErrors.FromException(exception);
        }
    }

    [McpServerTool(
        Name = "kb_list_images",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageListResponse))]
    [Description(
        "Lists supported images recursively under knowledgeBase.root, excluding Git and index files; cursor pagination defaults to 50.")]
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
            return ToolErrors.FromException(exception);
        }
    }

    [McpServerTool(Name = "kb_load_image")]
    [Description(
        "Returns an MCP image content block and a separate text content block containing JSON metadata (path, mimeType, bytes and sha256 of the original file). GIF images produce a PNG preview of the first frame without animation; JSON metadata still describes the original GIF. The complete multimodal result is not represented by a JSON outputSchema.")]
    public async Task<CallToolResult> LoadImageAsync(
        [Description(
            "Root-relative image path, for example chapters/diagram.png.")]
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
            // GIF image blocks are not reliably exposed by every MCP client.
            // Supply a PNG preview of the first frame without modifying the file.
            var isGif = loaded.Metadata.MimeType == "image/gif";
            var imageBytes = isGif
                ? GifPreview.CreatePng(loaded.Data)
                : loaded.Data;
            var imageMimeType = isGif
                ? "image/png"
                : loaded.Metadata.MimeType;
            return new CallToolResult
            {
                Content =
                [
                    ImageContentBlock.FromBytes(
                        imageBytes,
                        imageMimeType),
                    new TextContentBlock
                    {
                        Text = metadataJson
                    }
                ]
            };
        }
        catch (Exception exception) when (
            exception is WorkspaceImageException
                or KnowledgeBaseAccessException)
        {
            return ToolErrors.FromException(exception);
        }
    }

    [McpServerTool(
        Name = "kb_delete_image",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageDeleteResponse))]
    [Description(
        "Deletes one supported binary image under knowledgeBase.root without checking or changing Markdown references, index or parent directories. Requires allowWrites=true.")]
    public async Task<CallToolResult> DeleteImageAsync(
        [Description(
            "Root-relative image file path.")]
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
            return ToolErrors.FromException(exception);
        }
    }

    [McpServerTool(
        Name = "kb_move_image",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImageMoveResponse))]
    [Description(
        "Moves a supported image only within the configured knowledgeBase.root. " +
        "Paths outside the configured root are never allowed. " +
        "Never updates Markdown references or the index, and never overwrites an existing target. Repair links separately with kb_patch.")]
    public async Task<CallToolResult> MoveImageAsync(
        [Description(
            "Binary image move using root-relative sourcePath and targetPath; optional expectedSha256 checks for source changes.")]
        MoveImageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await imageMoves.MoveAsync(
                request,
                cancellationToken);
            return Structured(response);
        }
        catch (Exception exception) when (
            exception is WorkspaceImageException
                or KnowledgeBaseAccessException
                or DocumentConflictException)
        {
            return ToolErrors.FromException(exception);
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
        => ToolErrors.Create("INVALID_ARGUMENT", message);
}
