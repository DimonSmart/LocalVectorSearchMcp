using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkspaceImageServerLayerTests
{
    [Fact]
    public void WorkspaceImageMcpToolsExposeOnlyProductionImageSurface()
    {
        var tools = typeof(WorkspaceImageMcpTools)
            .GetMethods()
            .Select(method => new
            {
                Method = method,
                Attribute = method
                    .GetCustomAttributes(
                        typeof(McpServerToolAttribute),
                        false)
                    .Cast<McpServerToolAttribute>()
                    .SingleOrDefault()
            })
            .Where(item => item.Attribute is not null)
            .ToArray();

        Assert.Equal(
            [
                "kb_delete_image",
                "kb_list_images",
                "kb_load_image",
                "kb_move_image",
                "kb_save_image"
            ],
            tools
                .Select(item => item.Attribute!.Name)
                .OfType<string>()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        var loadTool = Assert.Single(
            tools,
            item => item.Attribute!.Name == "kb_load_image");
        Assert.False(loadTool.Attribute!.UseStructuredContent);
        Assert.Null(loadTool.Attribute.OutputSchemaType);

        Assert.All(
            tools.Where(item =>
                item.Attribute!.Name != "kb_load_image"),
            item =>
            {
                Assert.True(item.Attribute!.UseStructuredContent);
                Assert.NotNull(item.Attribute.OutputSchemaType);
            });
    }

    [Fact]
    public void MoveImageRequestExposesOnlySupportedParameters()
    {
        Assert.Equal(
            [
                nameof(MoveImageRequest.SourcePath),
                nameof(MoveImageRequest.TargetPath),
                nameof(MoveImageRequest.ExpectedSha256)
            ],
            typeof(MoveImageRequest)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray());
    }

    [Fact]
    public async Task LoadImageReturnsImageContentBeforeTextMetadata()
    {
        byte[] data =
        [
            0x89, 0x50, 0x4e, 0x47,
            0x0d, 0x0a, 0x1a, 0x0a
        ];
        var service = new RecordingImageService
        {
            ImageToLoad = new LoadedImage(
                new ImageLoadResponse(
                    "images/red-circle.png",
                    "image/png",
                    data.LongLength,
                    "abc123"),
                data)
        };
        var tools = new WorkspaceImageMcpTools(
            service,
            new RecordingImageMoveService());

        var result = await tools.LoadImageAsync(
            "images/red-circle.png",
            CancellationToken.None);

        Assert.False(result.IsError is true);
        Assert.Null(result.StructuredContent);
        Assert.Equal(2, result.Content.Count);

        var image = Assert.IsType<ImageContentBlock>(
            result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(data, image.DecodedData.ToArray());

        var metadata = Assert.IsType<TextContentBlock>(
            result.Content[1]);
        using var metadataDocument = JsonDocument.Parse(metadata.Text);
        var root = metadataDocument.RootElement;
        Assert.Equal("images/red-circle.png", root.GetProperty("path").GetString());
        Assert.Equal("image/png", root.GetProperty("mimeType").GetString());
        Assert.Equal("abc123", root.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task MalformedOpenAiFileParameterReturnsControlledErrorBeforeService()
    {
        var service = new RecordingImageService();
        var tools = new WorkspaceImageMcpTools(
            service,
            new RecordingImageMoveService());
        var malformed = new OpenAiFileParameter();

        var result = await tools.SaveImageAsync(
            malformed,
            CancellationToken.None);

        Assert.True(result.IsError is true);
        Assert.Equal(0, service.SaveCalls);
        Assert.IsType<TextContentBlock>(
            Assert.Single(result.Content));
    }

    private sealed class RecordingImageMoveService :
        IWorkspaceImageMoveService
    {
        public Task<ImageMoveResponse> MoveAsync(
            MoveImageRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingImageService :
        IWorkspaceImageService
    {
        public int SaveCalls { get; private set; }

        public LoadedImage? ImageToLoad { get; init; }

        public Task<ImageSaveResponse> SaveAsync(
            SaveImageRequest request,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            throw new InvalidOperationException();
        }

        public Task<ImageListResponse> ListAsync(
            string? cursor,
            int? pageSize,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedImage> LoadAsync(
            string path,
            CancellationToken cancellationToken)
            => Task.FromResult(
                ImageToLoad
                ?? throw new NotSupportedException());

        public Task<ImageDeleteResponse> DeleteAsync(
            string path,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
