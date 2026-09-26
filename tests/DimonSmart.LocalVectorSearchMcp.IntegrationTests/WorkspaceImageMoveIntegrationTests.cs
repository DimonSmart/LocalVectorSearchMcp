using System.Security.Cryptography;
using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class WorkspaceImageMoveIntegrationTests
{
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4e, 0x47,
        0x0d, 0x0a, 0x1a, 0x0a,
        0x00, 0x01, 0x02, 0x03
    ];

    private static readonly byte[] JpegBytes =
    [
        0xff, 0xd8, 0xff, 0xe0,
        0x00, 0x10, 0x4a, 0x46
    ];

    [Fact]
    public async Task MoveCanCrossWorkspaceFoldersAndPreservesSha()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(
            temp.Path,
            "draft",
            "a.png");
        Directory.CreateDirectory(
            Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(
            source,
            PngBytes,
            TestContext.Current.CancellationToken);
        var services = CreateServices(temp.Path);

        var response = await services.Service.MoveAsync(
            new MoveImageRequest(
                "draft/a.png",
                "book/images/a.png"),
            TestContext.Current.CancellationToken);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(
            temp.Path,
            "book",
            "images",
            "a.png")));
        Assert.Equal(
            Hash(PngBytes),
            response.Sha256);
        Assert.Equal(0, response.ReferencesUpdated);
        Assert.Empty(response.DocumentsUpdated);
        Assert.Empty(services.Scheduler.Paths);
        Assert.True(response.IndexSynchronized);
    }

    [Fact]
    public async Task MoveUpdatesInlineReferencesAndPreservesMarkdownRepresentation()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "chapters"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var markdownPath = Path.Combine(
            temp.Path,
            "chapters",
            "chapter.md");
        const string markdown =
            "# H\r\n" +
            "![Alt](../images/a.png \"Title\")\r\n" +
            "![Angle](<../images/a.png>)\r\n" +
            "\u0060![Code](../images/a.png)\u0060\r\n" +
            "Old path ../images/a.png\r\n" +
            "![Remote](https://example.test/images/a.png)\r\n";
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(markdown))
            .ToArray();
        await File.WriteAllBytesAsync(
            markdownPath,
            bytes,
            TestContext.Current.CancellationToken);
        var services = CreateServices(temp.Path);

        var response = await services.Service.MoveAsync(
            new MoveImageRequest(
                "images/a.png",
                "illustrations/b.png"),
            TestContext.Current.CancellationToken);

        var actual = await File.ReadAllBytesAsync(
            markdownPath,
            TestContext.Current.CancellationToken);
        Assert.True(
            actual.AsSpan().StartsWith(
                Encoding.UTF8.Preamble));
        var text = Encoding.UTF8.GetString(
            actual.AsSpan(Encoding.UTF8.Preamble.Length));
        Assert.Equal(
            "# H\r\n" +
            "![Alt](../illustrations/b.png \"Title\")\r\n" +
            "![Angle](<../illustrations/b.png>)\r\n" +
            "\u0060![Code](../images/a.png)\u0060\r\n" +
            "Old path ../images/a.png\r\n" +
            "![Remote](https://example.test/images/a.png)\r\n",
            text);
        Assert.Equal(2, response.ReferencesUpdated);
        Assert.Equal(
            ["chapters/chapter.md"],
            response.DocumentsUpdated);
        Assert.Equal(
            ["chapters/chapter.md"],
            services.Scheduler.Paths);
        Assert.False(response.IndexSynchronized);
    }

    [Fact]
    public async Task MoveUpdatesMultipleDocumentsWithDifferentRelativePaths()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "book", "nested"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "root.md"),
            "![Root](images/a.png)\n![Again](./images/a.png)\n",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "book", "nested", "chapter.md"),
            "![Nested](../../images/a.png)\n",
            TestContext.Current.CancellationToken);
        var services = CreateServices(temp.Path);

        var response = await services.Service.MoveAsync(
            new MoveImageRequest(
                "images/a.png",
                "assets/figures/a.png"),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, response.ReferencesUpdated);
        Assert.Equal(
            ["book/nested/chapter.md", "root.md"],
            response.DocumentsUpdated
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            ["book/nested/chapter.md", "root.md"],
            services.Scheduler.Paths
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            "![Root](assets/figures/a.png)\n![Again](./assets/figures/a.png)\n",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "root.md"),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "![Nested](../../assets/figures/a.png)\n",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "book", "nested", "chapter.md"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateReferencesFalseMovesOnlyBinary()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "book.md"),
            "![A](images/a.png)",
            TestContext.Current.CancellationToken);
        var services = CreateServices(temp.Path);

        var response = await services.Service.MoveAsync(
            new MoveImageRequest(
                "images/a.png",
                "images/b.png",
                UpdateReferences: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "![A](images/a.png)",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "book.md"),
                TestContext.Current.CancellationToken));
        Assert.Equal(0, response.ReferencesUpdated);
        Assert.Empty(services.Scheduler.Paths);
    }

    [Fact]
    public async Task IncludeExcludeLimitsReferenceUpdates()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "book"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "book", "included.md"),
            "![A](../images/a.png)",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "book", "excluded.md"),
            "![A](../images/a.png)",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "outside.md"),
            "![A](images/a.png)",
            TestContext.Current.CancellationToken);
        var services = CreateServices(
            temp.Path,
            ["book/*.md"],
            ["book/excluded.md"]);

        var response = await services.Service.MoveAsync(
            new MoveImageRequest(
                "images/a.png",
                "illustrations/a.png"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, response.ReferencesUpdated);
        Assert.Equal(
            "![A](../illustrations/a.png)",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "book", "included.md"),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "![A](../images/a.png)",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "book", "excluded.md"),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "![A](images/a.png)",
            await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "outside.md"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WorkspaceBoundaryTargetCollisionAndSamePathAreRejected()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "existing.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var services = CreateServices(temp.Path);
        var cancellationToken =
            TestContext.Current.CancellationToken;

        foreach (var request in new[]
                 {
                     new MoveImageRequest(
                         "../a.png",
                         "images/b.png"),
                     new MoveImageRequest(
                         "images/a.png",
                         "../outside.png"),
                     new MoveImageRequest(
                         "images/a.png",
                         Path.Combine(
                             temp.Path,
                             "absolute.png"))
                 })
        {
            await Assert.ThrowsAsync<KnowledgeBaseAccessException>(
                () => services.Service.MoveAsync(
                    request,
                    cancellationToken));
        }

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png",
                    "images/existing.png"),
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png",
                    "images/a.png"),
                cancellationToken));
        Assert.True(
            File.Exists(Path.Combine(
                temp.Path,
                "images",
                "a.png")));
    }

    [Fact]
    public async Task FormatsAndExpectedShaAreValidatedBeforeChanges()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var services = CreateServices(temp.Path);
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png",
                    "images/a.jpg"),
                cancellationToken));
        await Assert.ThrowsAsync<DocumentConflictException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png",
                    "images/b.png",
                    "00"),
                cancellationToken));
        Assert.True(
            File.Exists(Path.Combine(
                temp.Path,
                "images",
                "a.png")));
        Assert.False(
            File.Exists(Path.Combine(
                temp.Path,
                "images",
                "b.png")));

        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "bad.png"),
            [1, 2, 3, 4],
            cancellationToken);
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/bad.png",
                    "images/bad2.png"),
                cancellationToken));

        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "photo.jpg"),
            JpegBytes,
            cancellationToken);
        var jpeg = await services.Service.MoveAsync(
            new MoveImageRequest(
                "images/photo.jpg",
                "images/photo.jpeg"),
            cancellationToken);
        Assert.Equal(
            Hash(JpegBytes),
            jpeg.Sha256);
    }

    [Fact]
    public async Task MissingSourceSourceDirectoryAndTargetDirectoryAreRejected()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images", "folder"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var services = CreateServices(temp.Path);
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/missing.png",
                    "images/b.png"),
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/folder",
                    "images/b.png"),
                cancellationToken));
        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png",
                    "images/folder"),
                cancellationToken));
    }

    [Fact]
    public async Task WritesDisabledRejectsMoveWithoutFilesystemChanges()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var services = CreateServices(
            temp.Path,
            allowWrites: false);

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png",
                    "images/b.png"),
                TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(
            temp.Path,
            "images",
            "a.png")));
        Assert.False(File.Exists(Path.Combine(
            temp.Path,
            "images",
            "b.png")));
    }

    [Fact]
    public async Task MarkdownChangeDuringPreflightIsRetriedAndPreserved()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var markdownPath = Path.Combine(temp.Path, "book.md");
        await File.WriteAllTextAsync(
            markdownPath,
            "![A](images/a.png)\n",
            TestContext.Current.CancellationToken);

        var config = BuildConfig(temp.Path);
        var guard = new KnowledgeBasePathGuard(config);
        var inner = new MarkdownImageReferenceUpdater(
            config,
            guard);
        var updater = new MutatingUpdater(
            inner,
            () => File.AppendAllText(
                markdownPath,
                "user edit\n"));
        var scheduler = new RecordingScheduler();
        var service = new WorkspaceImageMoveService(
            config,
            guard,
            new MarkdownDocumentLoader(),
            updater,
            scheduler);

        var response = await service.MoveAsync(
            new MoveImageRequest(
                "images/a.png",
                "images/b.png"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, response.ReferencesUpdated);
        Assert.Equal(
            "![A](images/b.png)\nuser edit\n",
            await File.ReadAllTextAsync(
                markdownPath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PartialMarkdownCommitRollsBackImageAndDocuments()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var first = Path.Combine(temp.Path, "a.md");
        var second = Path.Combine(temp.Path, "b.md");
        await File.WriteAllTextAsync(
            first,
            "![A](images/a.png)",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            second,
            "![B](images/a.png)",
            TestContext.Current.CancellationToken);

        var services = CreateServices(
            temp.Path,
            fileOperations: new FaultingFileOperations(5));

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => services.Service.MoveAsync(
                new MoveImageRequest(
                    "images/a.png",
                    "images/b.png"),
                TestContext.Current.CancellationToken));

        Assert.True(
            File.Exists(Path.Combine(
                temp.Path,
                "images",
                "a.png")));
        Assert.False(
            File.Exists(Path.Combine(
                temp.Path,
                "images",
                "b.png")));
        Assert.Equal(
            "![A](images/a.png)",
            await File.ReadAllTextAsync(
                first,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "![B](images/a.png)",
            await File.ReadAllTextAsync(
                second,
                TestContext.Current.CancellationToken));
        Assert.Empty(services.Scheduler.Paths);
        Assert.Empty(
            Directory.GetFiles(
                temp.Path,
                "*.move-image.*",
                SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExistingSymlinkInTargetPathIsRejectedWhenSupported()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "images"));
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "images", "a.png"),
            PngBytes,
            TestContext.Current.CancellationToken);
        var external = Path.Combine(
            Path.GetTempPath(),
            $"image-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(external);
        var link = Path.Combine(temp.Path, "linked");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(
                    link,
                    external);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException
                    or IOException
                    or PlatformNotSupportedException)
            {
                return;
            }

            var services = CreateServices(temp.Path);
            await Assert.ThrowsAsync<KnowledgeBaseAccessException>(
                () => services.Service.MoveAsync(
                    new MoveImageRequest(
                        "images/a.png",
                        "linked/b.png"),
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            try
            {
                if (Directory.Exists(link))
                {
                    Directory.Delete(link);
                }
            }
            catch
            {
            }

            try
            {
                Directory.Delete(
                    external,
                    recursive: true);
            }
            catch
            {
            }
        }
    }

    private static TestServices CreateServices(
        string root,
        IReadOnlyList<string>? include = null,
        IReadOnlyList<string>? exclude = null,
        bool allowWrites = true,
        IWorkspaceImageMoveFileOperations? fileOperations = null)
    {
        var config = BuildConfig(
            root,
            include,
            exclude,
            allowWrites);
        var guard = new KnowledgeBasePathGuard(config);
        var scheduler = new RecordingScheduler();
        var updater = new MarkdownImageReferenceUpdater(
            config,
            guard);
        var service = fileOperations is null
            ? new WorkspaceImageMoveService(
                config,
                guard,
                new MarkdownDocumentLoader(),
                updater,
                scheduler)
            : new WorkspaceImageMoveService(
                config,
                guard,
                new MarkdownDocumentLoader(),
                updater,
                scheduler,
                fileOperations);
        return new TestServices(
            service,
            scheduler);
    }

    private static LocalVectorSearchMcpConfig BuildConfig(
        string root,
        IReadOnlyList<string>? include = null,
        IReadOnlyList<string>? exclude = null,
        bool allowWrites = true)
        => new()
        {
            Storage = new StorageConfig
            {
                Path = Path.Combine(
                    root,
                    "index.db")
            },
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = root,
                AllowWrites = allowWrites,
                Include = include ?? ["**/*.md"],
                Exclude = exclude ?? []
            }
        };

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();

    private sealed record TestServices(
        WorkspaceImageMoveService Service,
        RecordingScheduler Scheduler);

    private sealed class RecordingScheduler :
        IWorkspaceIndexSynchronizationScheduler
    {
        public List<string> Paths { get; } = [];

        public void Schedule(string relativePath)
            => Paths.Add(relativePath);
    }

    private sealed class MutatingUpdater(
        IMarkdownImageReferenceUpdater inner,
        Action mutation) : IMarkdownImageReferenceUpdater
    {
        private int mutated;

        public MarkdownImageReferenceUpdate Update(
            string markdown,
            string documentPath,
            string sourcePath,
            string targetPath)
        {
            var result = inner.Update(
                markdown,
                documentPath,
                sourcePath,
                targetPath);
            if (Interlocked.Exchange(
                    ref mutated,
                    1) == 0)
            {
                mutation();
            }

            return result;
        }
    }

    private sealed class FaultingFileOperations(
        int failOnMove) : IWorkspaceImageMoveFileOperations
    {
        private int moveCount;

        public void Move(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            if (Interlocked.Increment(
                    ref moveCount) == failOnMove)
            {
                throw new IOException(
                    "Injected move failure.");
            }

            File.Move(
                sourcePath,
                destinationPath,
                overwrite);
        }

        public void CreateDirectory(string path)
            => Directory.CreateDirectory(path);

        public void DeleteDirectory(string path)
            => Directory.Delete(path);

        public void DeleteFile(string path)
            => File.Delete(path);
    }
}
