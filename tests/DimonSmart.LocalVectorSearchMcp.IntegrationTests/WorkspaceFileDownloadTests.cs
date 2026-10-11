using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol.Protocol;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkspaceFileDownloadTests
{
    [Fact]
    public async Task DownloadAsync_ReturnsOriginalBytesAndMetadataForPermittedFile()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 0, 1, 2, 3 };
        Directory.CreateDirectory(Path.Combine(directory.Path, "exports"));
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "exports", "book.pdf"), bytes, TestContext.Current.CancellationToken);
        var service = CreateService(directory.Path);

        var downloaded = await service.DownloadAsync("exports/book.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("exports/book.pdf", downloaded.Path);
        Assert.Equal("application/pdf", downloaded.MimeType);
        Assert.Equal(bytes, downloaded.Data);
        Assert.Equal("054edec1d0211f624fed0cbca9d4f9400b0e491c43742af2c5b0abebf0c990d8", downloaded.Sha256);
    }

    [Fact]
    public async Task DownloadAsync_RejectsGitMetadataAndMissingFiles()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path);

        await Assert.ThrowsAsync<KnowledgeBaseAccessException>(() => service.DownloadAsync(".git/config", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<WorkspaceFileNotFoundException>(() => service.DownloadAsync("missing.pdf", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadFileAsync_ReturnsEmbeddedResourceBeforeMetadata()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var tool = new WorkspaceFileMcpTools(new FixedDownloadService(new DownloadedWorkspaceFile(
            "book.pdf", "application/pdf", bytes.Length, "hash", bytes)));

        var result = await tool.DownloadFileAsync("book.pdf", TestContext.Current.CancellationToken);

        Assert.False(result.IsError is true);
        Assert.Null(result.StructuredContent);
        var resource = Assert.IsType<EmbeddedResourceBlock>(result.Content[0]);
        var blob = Assert.IsType<BlobResourceContents>(resource.Resource);
        Assert.Equal(bytes, blob.DecodedData.ToArray());
        Assert.Equal("application/pdf", blob.MimeType);
        var metadata = Assert.IsType<TextContentBlock>(result.Content[1]);
        using var json = JsonDocument.Parse(metadata.Text);
        Assert.Equal("book.pdf", json.RootElement.GetProperty("path").GetString());
        Assert.Equal("hash", json.RootElement.GetProperty("sha256").GetString());
    }

    private static WorkspaceFileDownloadService CreateService(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = root },
            Storage = new StorageConfig { Path = Path.Combine(root, ".index.db") }
        };
        return new WorkspaceFileDownloadService(new KnowledgeBasePathGuard(config));
    }

    private sealed class FixedDownloadService(DownloadedWorkspaceFile file) : IWorkspaceFileDownloadService
    {
        public Task<DownloadedWorkspaceFile> DownloadAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(file);
    }
}
