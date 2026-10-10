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

    private const string AnimatedGifBase64 =
        "R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAAgABAAAIBQABAAgIACH5BAEKAAEALAAAAAACAAEAgQD/AAAAAAAAAAAAAAgFAAEACAgAOw==";

    [Fact]
    public async Task LoadAnimatedGifReturnsFirstFrameAsPngWithoutChangingOriginalMetadata()
    {
        var gif = Convert.FromBase64String(AnimatedGifBase64);
        var service = new RecordingImageService
        {
            ImageToLoad = new LoadedImage(
                new ImageLoadResponse(
                    "images/animated.gif",
                    "image/gif",
                    gif.LongLength,
                    "original-gif-sha256"),
                gif)
        };
        var tools = new WorkspaceImageMcpTools(
            service,
            new RecordingImageMoveService());

        var result = await tools.LoadImageAsync(
            "images/animated.gif",
            CancellationToken.None);

        Assert.False(result.IsError is true);
        Assert.Null(result.StructuredContent);
        Assert.Equal(2, result.Content.Count);
        var preview = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", preview.MimeType);
        var png = preview.DecodedData.ToArray();
        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
            png[..8]);
        var pixels = StbImageSharp.ImageResult.FromMemory(
            png,
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(2, pixels.Width);
        Assert.Equal(1, pixels.Height);
        Assert.Equal(
            new byte[] { 255, 0, 0, 255 },
            pixels.Data[..4]);

        var metadata = Assert.IsType<TextContentBlock>(result.Content[1]);
        using var document = JsonDocument.Parse(metadata.Text);
        var original = document.RootElement;
        Assert.Equal("images/animated.gif", original.GetProperty("path").GetString());
        Assert.Equal("image/gif", original.GetProperty("mimeType").GetString());
        Assert.Equal(gif.LongLength, original.GetProperty("bytes").GetInt64());
        Assert.Equal("original-gif-sha256", original.GetProperty("sha256").GetString());
        Assert.Equal(Convert.FromBase64String(AnimatedGifBase64), gif);
    }

    [Fact]
    public async Task LoadGifWithInvalidPayloadReturnsControlledError()
    {
        var invalid = Convert.FromBase64String(AnimatedGifBase64)[..12];
        var service = new RecordingImageService
        {
            ImageToLoad = new LoadedImage(
                new ImageLoadResponse(
                    "images/truncated.gif",
                    "image/gif",
                    invalid.LongLength,
                    "sha"),
                invalid)
        };
        var tools = new WorkspaceImageMcpTools(
            service,
            new RecordingImageMoveService());

        var result = await tools.LoadImageAsync(
            "images/truncated.gif",
            CancellationToken.None);

        Assert.True(result.IsError is true);
        Assert.Single(result.Content);
        var error = Assert.IsType<TextContentBlock>(result.Content[0]);
        using var json = JsonDocument.Parse(error.Text);
        Assert.Equal(
            "UNSUPPORTED_FORMAT",
            json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LoadGifWithExcessiveDimensionsRejectsBeforeDecoding()
    {
        var oversized = Convert.FromBase64String(AnimatedGifBase64);
        oversized[6] = 255;
        oversized[7] = 255;
        oversized[8] = 255;
        oversized[9] = 255;
        var service = new RecordingImageService
        {
            ImageToLoad = new LoadedImage(
                new ImageLoadResponse(
                    "images/oversized.gif",
                    "image/gif",
                    oversized.LongLength,
                    "sha"),
                oversized)
        };
        var tools = new WorkspaceImageMcpTools(
            service,
            new RecordingImageMoveService());

        var result = await tools.LoadImageAsync(
            "images/oversized.gif",
            CancellationToken.None);

        Assert.True(result.IsError is true);
        var error = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var json = JsonDocument.Parse(error.Text);
        Assert.Equal(
            "INVALID_ARGUMENT",
            json.RootElement.GetProperty("code").GetString());
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
