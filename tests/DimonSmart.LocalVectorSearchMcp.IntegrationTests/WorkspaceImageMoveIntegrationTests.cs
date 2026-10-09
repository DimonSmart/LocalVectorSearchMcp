using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkspaceImageMoveIntegrationTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 1, 2, 3];

    [Fact]
    public async Task MoveOnlyChangesBinaryFileAndCreatesDestinationDirectories()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "images", "a.png"), Png, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "document.md"), "![A](images/a.png)", TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        var response = await service.MoveAsync(new MoveImageRequest("images/a.png", "assets/diagrams/a.png"), TestContext.Current.CancellationToken);
        Assert.Equal("images/a.png", response.PreviousPath);
        Assert.Equal("assets/diagrams/a.png", response.Path);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Png)).ToLowerInvariant(), response.Sha256);
        Assert.False(File.Exists(Path.Combine(temp.Path, "images", "a.png")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "assets", "diagrams", "a.png")));
        Assert.Equal("![A](images/a.png)", await File.ReadAllTextAsync(Path.Combine(temp.Path, "document.md"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitLegacyFlag(bool updateReferences)
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "images", "a.png"), Png, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        if (updateReferences)
        {
            await Assert.ThrowsAsync<WorkspaceImageException>(() => service.MoveAsync(
                new MoveImageRequest("images/a.png", "assets/a.png", UpdateReferences: true),
                TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(temp.Path, "images", "a.png")));
        }
        else
        {
            await service.MoveAsync(new MoveImageRequest("images/a.png", "assets/a.png", UpdateReferences: false), TestContext.Current.CancellationToken);
            Assert.True(File.Exists(Path.Combine(temp.Path, "assets", "a.png")));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("123456789012345678901234567890123456789012345678901234567890123z")]
    public async Task InvalidHashIsRejected(string hash)
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "images", "a.png"), Png, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<WorkspaceImageException>(() => CreateService(temp.Path).MoveAsync(
            new MoveImageRequest("images/a.png", "assets/a.png", ExpectedSha256: hash),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingDestinationNeverOverwritten()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "images", "a.png"), Png, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "images", "b.png"), [1, 2, 3], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<WorkspaceImageException>(() => CreateService(temp.Path).MoveAsync(
            new MoveImageRequest("images/a.png", "images/b.png"), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(temp.Path, "images", "b.png"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsSymlinkDirectory()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "images", "a.png"), Png, TestContext.Current.CancellationToken);
        try { Directory.CreateSymbolicLink(Path.Combine(temp.Path, "linked"), Path.Combine(temp.Path, "images")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; }
        await Assert.ThrowsAsync<KnowledgeBaseAccessException>(() => CreateService(temp.Path).MoveAsync(
            new MoveImageRequest("images/a.png", "linked/a.png"), TestContext.Current.CancellationToken));
    }

    private static WorkspaceImageMoveService CreateService(string root, bool allowWrites = true)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            Storage = new StorageConfig { Path = Path.Combine(root, "index.db") },
            KnowledgeBase = new KnowledgeBaseConfig { Root = root, AllowWrites = allowWrites }
        };
        return new WorkspaceImageMoveService(config, new KnowledgeBasePathGuard(config));
    }
}
