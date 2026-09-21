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
                "kb_save_image"
            ],
            tools
                .Select(item => item.Attribute!.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        Assert.All(
            tools,
            item =>
            {
                Assert.True(item.Attribute!.UseStructuredContent);
                Assert.NotNull(item.Attribute.OutputSchemaType);
            });
    }

    [Fact]
    public async Task MalformedOpenAiFileParameterReturnsControlledErrorBeforeService()
    {
        var service = new RecordingImageService();
        var tools = new WorkspaceImageMcpTools(service);
        var malformed = new OpenAiFileParameter("", "")
        {
            InputError = "expected a JSON object, received String."
        };

        var result = await tools.SaveImageAsync(
            malformed,
            CancellationToken.None);

        Assert.True(result.IsError is true);
        Assert.Equal(0, service.SaveCalls);
        Assert.IsType<TextContentBlock>(
            Assert.Single(result.Content));
    }

    private sealed class RecordingImageService :
        IWorkspaceImageService
    {
        public int SaveCalls { get; private set; }

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
            => throw new NotSupportedException();

        public Task<ImageDeleteResponse> DeleteAsync(
            string path,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
