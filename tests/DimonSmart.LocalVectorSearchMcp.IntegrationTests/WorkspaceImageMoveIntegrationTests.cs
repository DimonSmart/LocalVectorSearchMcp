using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkspaceImageMoveIntegrationTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01];
    private static readonly byte[] Jpeg =
        [0xff, 0xd8, 0xff, 0xe0, 0x01];

    [Theory]
    [InlineData("images/a.png", "chapters/a.png")]
    [InlineData("a.png", "images/nested/a.png")]
    [InlineData("chapter/a.png", "chapter/b.png")]
    public async Task MovesOnlyBinaryFile(string sourcePath, string targetPath)
    {
        using var temp = new TemporaryDirectory();
        var source = Write(temp.Path, sourcePath, Png);
        var markdownPath = Path.Combine(temp.Path, "chapter.md");
        const string markdown = "![Image](images/a.png)\n";
        await File.WriteAllTextAsync(markdownPath, markdown);
        var service = CreateService(temp.Path);

        var moved = await service.MoveAsync(
            new MoveImageRequest(sourcePath, targetPath),
            TestContext.Current.CancellationToken);

        Assert.Equal(sourcePath, moved.PreviousPath);
        Assert.Equal(targetPath, moved.Path);
        Assert.Equal(Sha(Png), moved.Sha256);
        Assert.False(File.Exists(source));
        Assert.Equal(Png, await File.ReadAllBytesAsync(
            Path.Combine(temp.Path, targetPath.Replace(
                '/', Path.DirectorySeparatorChar))));
        Assert.Equal(markdown, await File.ReadAllTextAsync(markdownPath));
    }

    [Fact]
    public async Task RefusesDeprecatedAutomaticReferenceUpdate()
    {
        using var temp = new TemporaryDirectory();
        var source = Write(temp.Path, "images/a.png", Png);
        var service = CreateService(temp.Path);
        var error = await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.MoveAsync(
                new MoveImageRequest("images/a.png", "assets/a.png",
                    UpdateReferences: true),
                TestContext.Current.CancellationToken));
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task UpdateReferencesFalseIsBackwardCompatible()
    {
        using var temp = new TemporaryDirectory();
        Write(temp.Path, "images/a.png", Png);
        var service = CreateService(temp.Path);
        var result = await service.MoveAsync(
            new MoveImageRequest("images/a.png", "assets/a.png",
                UpdateReferences: false),
            TestContext.Current.CancellationToken);
        Assert.Equal("assets/a.png", result.Path);
    }

    [Fact]
    public async Task ExpectedSha256DetectsConflictAndMalformedValues()
    {
        using var temp = new TemporaryDirectory();
        Write(temp.Path, "images/a.png", Png);
        var service = CreateService(temp.Path);

        var invalid = await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.MoveAsync(
                new MoveImageRequest("images/a.png", "assets/a.png", "bad"),
                TestContext.Current.CancellationToken));
        Assert.Equal("INVALID_ARGUMENT", invalid.Code);

        var conflict = await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png", "assets/a.png", new string('0', 64)),
                TestContext.Current.CancellationToken));
        Assert.Equal("CONFLICT", conflict.Code);

        var result = await service.MoveAsync(
            new MoveImageRequest(
                "images/a.png", "assets/a.png", Sha(Png).ToUpperInvariant()),
            TestContext.Current.CancellationToken);
        Assert.Equal(Sha(Png), result.Sha256);
    }

    [Fact]
    public async Task ExistingTargetIsNotOverwritten()
    {
        using var temp = new TemporaryDirectory();
        Write(temp.Path, "images/a.png", Png);
        Write(temp.Path, "assets/a.png", Jpeg);
        var service = CreateService(temp.Path);
        var error = await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.MoveAsync(
                new MoveImageRequest("images/a.png", "assets/a.png"),
                TestContext.Current.CancellationToken));
        Assert.Equal("ALREADY_EXISTS", error.Code);
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(
            Path.Combine(temp.Path, "assets", "a.png")));
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("/outside.png")]
    [InlineData("C:/outside.png")]
    [InlineData(".git/image.png")]
    [InlineData("index.db-wal")]
    public async Task RejectsUnsafeTargets(string target)
    {
        using var temp = new TemporaryDirectory();
        Write(temp.Path, "images/a.png", Png);
        var service = CreateService(temp.Path);
        await Assert.ThrowsAsync<KnowledgeBaseAccessException>(
            () => service.MoveAsync(
                new MoveImageRequest("images/a.png", target),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsMismatchedTargetExtension()
    {
        using var temp = new TemporaryDirectory();
        Write(temp.Path, "images/a.png", Png);
        var service = CreateService(temp.Path);
        var error = await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.MoveAsync(
                new MoveImageRequest("images/a.png", "assets/a.jpg"),
                TestContext.Current.CancellationToken));
        Assert.Equal("UNSUPPORTED_FORMAT", error.Code);
    }

    [Fact]
    public async Task AllowsJpegExtensionAlias()
    {
        using var temp = new TemporaryDirectory();
        Write(temp.Path, "source.jpg", Jpeg);
        var service = CreateService(temp.Path);
        var result = await service.MoveAsync(
            new MoveImageRequest("source.jpg", "nested/target.jpeg"),
            TestContext.Current.CancellationToken);
        Assert.Equal(Sha(Jpeg), result.Sha256);
    }

    [Fact]
    public async Task ReadOnlyWorkspaceRejectsMove()
    {
        using var temp = new TemporaryDirectory();
        Write(temp.Path, "a.png", Png);
        var service = CreateService(temp.Path, false);
        var error = await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.MoveAsync(
                new MoveImageRequest("a.png", "b.png"),
                TestContext.Current.CancellationToken));
        Assert.Equal("PERMISSION_DENIED", error.Code);
    }

    private static WorkspaceImageMoveService CreateService(
        string root, bool allowWrites = true)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            Storage = new StorageConfig
            {
                Path = Path.Combine(root, "index.db")
            },
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = root,
                AllowWrites = allowWrites
            }
        };
        return new WorkspaceImageMoveService(
            config, new KnowledgeBasePathGuard(config));
    }

    private static string Write(string root, string path, byte[] bytes)
    {
        var absolute = Path.Combine(root, path.Replace(
            '/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, bytes);
        return absolute;
    }

    private static string Sha(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
