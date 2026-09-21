using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkspaceImageIntegrationTests
{
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4e, 0x47,
        0x0d, 0x0a, 0x1a, 0x0a,
        0x00, 0x01, 0x02, 0x03
    ];

    [Fact]
    public async Task SaveWithWritesDisabledDoesNotTouchFilesystem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var downloader = new FakeDownloader(PngBytes);
        var service = CreateService(
            temp.Path,
            allowWrites: false,
            downloader,
            out _,
            out _);

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.SaveAsync(
                SaveRequest("cover.png"),
                cancellationToken));

        Assert.Equal(0, downloader.CallCount);
        Assert.False(
            Directory.Exists(Path.Combine(temp.Path, "images")));
    }

    [Fact]
    public async Task ValidPngSaveReturnsMetadataAndAppearsAsAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var downloader = new FakeDownloader(PngBytes);
        var service = CreateService(
            temp.Path,
            allowWrites: true,
            downloader,
            out var config,
            out var guard);

        var saved = await service.SaveAsync(
            SaveRequest("cover", "Cover"),
            cancellationToken);

        Assert.Equal("images/cover.png", saved.Path);
        Assert.Equal("image/png", saved.MimeType);
        Assert.Equal(PngBytes.LongLength, saved.Bytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(PngBytes))
                .ToLowerInvariant(),
            saved.Sha256);
        Assert.Equal(
            "![Cover](images/cover.png)",
            saved.Markdown);
        Assert.Equal(
            PngBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(temp.Path, "images", "cover.png"),
                cancellationToken));
        Assert.Empty(
            Directory.GetFiles(
                Path.Combine(temp.Path, "images"),
                ".upload-*.tmp"));

        var navigation = new WorkspaceNavigationService(
            config,
            guard,
            new MarkdownDocumentLoader(),
            new MarkdownElementParser());
        var files = await navigation.ListFilesAsync(
            null,
            null,
            cancellationToken);
        var asset = Assert.Single(
            files.Files,
            file => file.RelativePath == "images/cover.png");
        Assert.Equal(WorkspaceFileKind.Asset, asset.Kind);
    }

    [Fact]
    public async Task ConcurrentSavesNeverOverwriteAndUseCollisionSuffixes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var service = CreateService(
            temp.Path,
            allowWrites: true,
            new FakeDownloader(PngBytes),
            out _,
            out _);

        var first = await service.SaveAsync(
            SaveRequest("same.png"),
            cancellationToken);
        var concurrent = await Task.WhenAll(
            Enumerable.Range(0, 5)
                .Select(_ => service.SaveAsync(
                    SaveRequest("same.png"),
                    cancellationToken)));

        var paths = new[] { first }
            .Concat(concurrent)
            .Select(item => item.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(6, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "images/same-2.png",
                "images/same-3.png",
                "images/same-4.png",
                "images/same-5.png",
                "images/same-6.png",
                "images/same.png"
            ],
            paths);

        foreach (var path in paths)
        {
            Assert.Equal(
                PngBytes,
                await File.ReadAllBytesAsync(
                    Path.Combine(
                        temp.Path,
                        path.Replace('/', Path.DirectorySeparatorChar)),
                    cancellationToken));
        }
    }

    [Fact]
    public async Task ListImagesIsRecursiveStableAndCursorSurvivesDeletedItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var images = Path.Combine(temp.Path, "images");
        Directory.CreateDirectory(Path.Combine(images, "nested"));
        await File.WriteAllBytesAsync(
            Path.Combine(images, "a.png"),
            PngBytes,
            cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(images, "B.JPG"),
            PngBytes,
            cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(images, "nested", "c.webp"),
            PngBytes,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(images, "ignored.txt"),
            "not image",
            cancellationToken);

        var service = CreateService(
            temp.Path,
            allowWrites: false,
            new FakeDownloader(PngBytes),
            out _,
            out _);

        var first = await service.ListAsync(
            null,
            1,
            cancellationToken);
        Assert.Equal("images/a.png", Assert.Single(first.Images).Path);
        Assert.NotNull(first.NextCursor);

        File.Delete(Path.Combine(images, "a.png"));
        var second = await service.ListAsync(
            first.NextCursor,
            1,
            cancellationToken);
        Assert.Equal("images/B.JPG", Assert.Single(second.Images).Path);
        Assert.Equal("image/jpeg", second.Images[0].MimeType);

        var final = await service.ListAsync(
            second.NextCursor,
            50,
            cancellationToken);
        Assert.Equal(
            ["images/nested/c.webp"],
            final.Images.Select(item => item.Path).ToArray());
        Assert.Null(final.NextCursor);

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.ListAsync(
                null,
                0,
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.ListAsync(
                "bad!",
                10,
                cancellationToken));
    }

    [Fact]
    public async Task MissingImagesDirectoryListsEmptyWhenReadOnly()
    {
        using var temp = new TemporaryDirectory();
        var service = CreateService(
            temp.Path,
            allowWrites: false,
            new FakeDownloader(PngBytes),
            out _,
            out _);

        var response = await service.ListAsync(
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.Empty(response.Images);
        Assert.Null(response.NextCursor);
    }

    [Fact]
    public async Task LoadValidatesNestedImageSignatureExtensionAndSize()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var nested = Path.Combine(temp.Path, "images", "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllBytesAsync(
            Path.Combine(nested, "ok.png"),
            PngBytes,
            cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(nested, "wrong.jpg"),
            PngBytes,
            cancellationToken);
        await using (var oversized = new FileStream(
            Path.Combine(nested, "big.png"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        {
            oversized.SetLength(
                WorkspaceImageService.MaxImageBytes + 1);
        }

        var service = CreateService(
            temp.Path,
            allowWrites: false,
            new FakeDownloader(PngBytes),
            out _,
            out _);

        var loaded = await service.LoadAsync(
            "images/nested/ok.png",
            cancellationToken);
        Assert.Equal("image/png", loaded.Metadata.MimeType);
        Assert.Equal(PngBytes.LongLength, loaded.Metadata.Bytes);
        Assert.Equal(PngBytes, loaded.Data);

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.LoadAsync(
                "images/nested/wrong.jpg",
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.LoadAsync(
                "images/nested/big.png",
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => service.LoadAsync(
                "images/nested/missing.png",
                cancellationToken));
    }

    [Fact]
    public async Task DeleteRequiresWritesAndOnlyDeletesImageFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var nested = Path.Combine(temp.Path, "images", "nested");
        Directory.CreateDirectory(nested);
        var imagePath = Path.Combine(nested, "delete.png");
        await File.WriteAllBytesAsync(
            imagePath,
            PngBytes,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(nested, "note.md"),
            "# note",
            cancellationToken);

        var readOnly = CreateService(
            temp.Path,
            allowWrites: false,
            new FakeDownloader(PngBytes),
            out _,
            out _);
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => readOnly.DeleteAsync(
                "images/nested/delete.png",
                cancellationToken));
        Assert.True(File.Exists(imagePath));

        var writable = CreateService(
            temp.Path,
            allowWrites: true,
            new FakeDownloader(PngBytes),
            out _,
            out _);
        var deleted = await writable.DeleteAsync(
            "images/nested/delete.png",
            cancellationToken);

        Assert.True(deleted.Deleted);
        Assert.Equal(
            "images/nested/delete.png",
            deleted.Path);
        Assert.False(File.Exists(imagePath));
        Assert.True(Directory.Exists(nested));

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => writable.DeleteAsync(
                "images/nested/delete.png",
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => writable.DeleteAsync(
                "images/nested",
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => writable.DeleteAsync(
                "images/nested/note.md",
                cancellationToken));
    }

    [Fact]
    public async Task ImagePathPolicyRejectsOutsidePathsAndSupportedSymlinkEscapes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var service = CreateService(
            temp.Path,
            allowWrites: false,
            new FakeDownloader(PngBytes),
            out _,
            out _);

        foreach (var path in new[]
                 {
                     "foo.png",
                     "docs/foo.png",
                     "../images/foo.png",
                     "images/../foo.png"
                 })
        {
            await Assert.ThrowsAsync<KnowledgeBaseAccessException>(
                () => service.LoadAsync(path, cancellationToken));
        }

        var images = Path.Combine(temp.Path, "images");
        Directory.CreateDirectory(images);
        var external = Path.Combine(
            Path.GetTempPath(),
            $"local-vector-image-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(
            external,
            PngBytes,
            cancellationToken);
        var link = Path.Combine(images, "link.png");
        try
        {
            try
            {
                File.CreateSymbolicLink(link, external);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException
                    or IOException
                    or PlatformNotSupportedException)
            {
                return;
            }

            await Assert.ThrowsAsync<KnowledgeBaseAccessException>(
                () => service.LoadAsync(
                    "images/link.png",
                    cancellationToken));
        }
        finally
        {
            try
            {
                if (File.Exists(link))
                {
                    File.Delete(link);
                }
            }
            catch
            {
            }

            try
            {
                File.Delete(external);
            }
            catch
            {
            }
        }
    }

    private static SaveImageRequest SaveRequest(
        string fileName,
        string? altText = null)
        => new(
            "https://example.test/image",
            "image/png",
            "source.png",
            fileName,
            altText);

    private static WorkspaceImageService CreateService(
        string root,
        bool allowWrites,
        IRemoteFileDownloader downloader,
        out LocalVectorSearchMcpConfig config,
        out KnowledgeBasePathGuard guard)
    {
        config = new LocalVectorSearchMcpConfig
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
        guard = new KnowledgeBasePathGuard(config);
        return new WorkspaceImageService(
            config,
            guard,
            downloader);
    }

    private sealed class FakeDownloader(byte[] bytes) :
        IRemoteFileDownloader
    {
        private int callCount;

        public int CallCount => callCount;

        public async Task<RemoteFileDownloadResult> DownloadAsync(
            string downloadUrl,
            string destinationPath,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            await File.WriteAllBytesAsync(
                destinationPath,
                bytes,
                cancellationToken);
            return new RemoteFileDownloadResult(
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes))
                    .ToLowerInvariant(),
                bytes.Take(16).ToArray(),
                "image/png");
        }
    }
}
