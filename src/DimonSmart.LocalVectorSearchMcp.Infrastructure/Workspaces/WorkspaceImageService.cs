using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class WorkspaceImageService(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard,
    IRemoteFileDownloader downloader) : IWorkspaceImageService
{
    private static readonly object FinalMoveGate = new();

    public const long MaxImageBytes = 25L * 1024 * 1024;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int BufferSize = 64 * 1024;

    public async Task<ImageSaveResponse> SaveAsync(SaveImageRequest request, CancellationToken cancellationToken)
    {
        EnsureWritesEnabled();
        cancellationToken.ThrowIfCancellationRequested();
        var directory = pathGuard.ValidateImageDirectory(request.Directory);
        var documentPath = request.DocumentPath;
        if (documentPath is not null) _ = pathGuard.ResolveMarkdownPath(pathGuard.ValidateImagePath(documentPath));
        ImageFileNamePolicy.ValidateRequestedFileName(request.FileName);
        var created = new List<string>();
        string? temp = null;
        try
        {
            await WorkspaceMutationGate.RunAsync(() =>
            {
                var relative = "";
                if (directory != ".")
                {
                    foreach (var part in directory.Split('/'))
                    {
                        relative = relative.Length == 0 ? part : relative + "/" + part;
                        var absolute = pathGuard.ResolveImageDirectory(relative);
                        if (!Directory.Exists(absolute))
                        {
                            Directory.CreateDirectory(absolute);
                            created.Add(absolute);
                        }
                        _ = pathGuard.ResolveImageDirectory(relative);
                    }
                }
                return Task.FromResult(0);
            }, cancellationToken);

            temp = pathGuard.ResolveWorkspacePath((directory == "." ? "" : directory + "/") +
                $".upload-{Guid.NewGuid():N}.tmp");
            RemoteFileDownloadResult download;
            try { download = await downloader.DownloadAsync(request.DownloadUrl, temp, MaxImageBytes, cancellationToken); }
            catch (WorkspaceImageException) { throw; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { throw new WorkspaceImageException("The temporary image file could not be written."); }

            var format = WorkspaceImageFormats.Detect(download.Prefix)
                ?? throw new WorkspaceImageException("The supplied file is not a supported PNG, JPEG, WebP, or GIF image.");
            WorkspaceImageFormats.ValidateDeclaredMime(request.DeclaredMimeType, format);
            var name = ImageFileNamePolicy.Resolve(request.FileName, request.SourceFileName, format);
            var path = await WorkspaceMutationGate.RunAsync(
                () => Task.FromResult(MoveWithoutOverwrite(temp, name, directory)), cancellationToken);
            temp = null;
            var link = documentPath is null ? path : BuildRelativeDestination(documentPath, path);
            return new ImageSaveResponse(path, format.MimeType, download.Bytes, download.Sha256,
                ImageFileNamePolicy.BuildMarkdown(link, request.AltText));
        }
        finally
        {
            if (temp is not null) TryDeleteFile(temp);
            foreach (var path in created.AsEnumerable().Reverse()) TryDeleteEmptyDirectory(path);
        }
    }

    private static string BuildRelativeDestination(string documentPath, string imagePath)
    {
        var source = documentPath.Split('/')[..^1];
        var destination = imagePath.Split('/');
        var common = 0;
        while (common < source.Length && common < destination.Length &&
               string.Equals(source[common], destination[common], StringComparison.Ordinal)) common++;
        return string.Join("/", Enumerable.Repeat("..", source.Length - common).Concat(destination.Skip(common)));
    }

    public Task<ImageListResponse> ListAsync(
        string? cursor,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize ?? DefaultPageSize;
        if (effectivePageSize is < 1 or > MaxPageSize)
        {
            throw new WorkspaceImageException(
                $"pageSize must be between 1 and {MaxPageSize}.");
        }

        var imagesDirectory = pathGuard.ResolveImageDirectory(".");
        if (!Directory.Exists(imagesDirectory))
            throw new WorkspaceImageException("Knowledge base root is unavailable.");
        var items = EnumerateImages(
                imagesDirectory,
                cancellationToken)
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ToList();

        var cursorPath = cursor is null ? null : ImageListCursor.Decode(cursor);
        if (cursorPath is not null)
        {
            try
            {
                pathGuard.ValidateImagePath(cursorPath);
                if (!WorkspaceImageFormats.TryFromExtension(Path.GetExtension(cursorPath), out _))
                    throw new WorkspaceImageException("Invalid image cursor path. Start listing again.");
            }
            catch (KnowledgeBaseAccessException)
            { throw new WorkspaceImageException("Invalid image cursor path. Start listing again."); }
        }
        var startIndex = cursorPath is null
            ? 0
            : FindStartIndex(items, cursorPath);

        var page = items
            .Skip(startIndex)
            .Take(effectivePageSize)
            .ToList();
        var hasMore = startIndex + page.Count < items.Count;
        var nextCursor = hasMore && page.Count > 0
            ? ImageListCursor.Encode(page[^1].Path)
            : null;

        return Task.FromResult(
            new ImageListResponse(page, nextCursor));
    }

    public async Task<LoadedImage> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var normalized = pathGuard.ValidateImagePath(path);
        var absolute = pathGuard.ResolveImagePath(normalized);
        if (Directory.Exists(absolute))
        {
            throw new WorkspaceImageException(
                $"Image path '{normalized}' is a directory.");
        }

        if (!File.Exists(absolute))
        {
            throw new WorkspaceImageException(
                $"Image '{normalized}' does not exist.");
        }

        var extension = Path.GetExtension(normalized);
        if (!WorkspaceImageFormats.TryFromExtension(
                extension,
                out var expectedFormat))
        {
            throw new WorkspaceImageException(
                $"Image '{normalized}' has an unsupported file extension.");
        }

        var fileInfo = new FileInfo(absolute);
        if (fileInfo.Length > MaxImageBytes)
        {
            throw new WorkspaceImageException(
                $"Image '{normalized}' exceeds the {MaxImageBytes} byte limit.");
        }

        var read = await ReadImageAsync(
            absolute,
            normalized,
            cancellationToken);
        var actualFormat = WorkspaceImageFormats.Detect(read.Prefix)
            ?? throw new WorkspaceImageException(
                $"Image '{normalized}' has an unsupported or invalid image signature.");

        if (actualFormat.Format != expectedFormat.Format)
        {
            throw new WorkspaceImageException(
                $"Image '{normalized}' extension does not match its detected {actualFormat.MimeType} format.");
        }

        return new LoadedImage(
            new ImageLoadResponse(
                normalized,
                actualFormat.MimeType,
                read.Bytes.LongLength,
                read.Sha256),
            read.Bytes);
    }

    public Task<ImageDeleteResponse> DeleteAsync(
        string path,
        CancellationToken cancellationToken)
        => WorkspaceMutationGate.RunAsync(
            () => DeleteCoreAsync(
                path,
                cancellationToken),
            cancellationToken);

    private Task<ImageDeleteResponse> DeleteCoreAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritesEnabled();

        var normalized = pathGuard.ValidateImagePath(path);
        var absolute = pathGuard.ResolveImagePath(normalized);
        if (Directory.Exists(absolute))
        {
            throw new WorkspaceImageException(
                $"Image path '{normalized}' is a directory.");
        }

        if (!File.Exists(absolute))
        {
            throw new WorkspaceImageException(
                $"Image '{normalized}' does not exist.");
        }

        if (!WorkspaceImageFormats.TryFromExtension(
                Path.GetExtension(normalized),
                out _))
        {
            throw new WorkspaceImageException(
                $"Image '{normalized}' has an unsupported file extension.");
        }

        try
        {
            File.Delete(absolute);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            throw new WorkspaceImageException(
                $"Image '{normalized}' could not be deleted.");
        }

        return Task.FromResult(
            new ImageDeleteResponse(normalized, true));
    }

    private string MoveWithoutOverwrite(
        string temporaryPath,
        string requestedName,
        string directory)
    {
        lock (FinalMoveGate)
        {
            for (var suffix = 1; ; suffix++)
            {
                var fileName = suffix == 1
                    ? requestedName
                    : ImageFileNamePolicy.WithCollisionSuffix(
                        requestedName,
                        suffix);
                var relativePath = directory == "." ? fileName : $"{directory}/{fileName}";
                var absolutePath = pathGuard.ResolveImagePath(relativePath);
                _ = pathGuard.ResolveWorkspacePath(Path.GetRelativePath(
                    config.KnowledgeBase.Root, temporaryPath).Replace('\\', '/'));

                try
                {
                    File.Move(
                        temporaryPath,
                        absolutePath,
                        overwrite: false);
                    return relativePath;
                }
                catch (IOException) when (
                    File.Exists(absolutePath)
                    || Directory.Exists(absolutePath))
                {
                    continue;
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException)
                {
                    throw new WorkspaceImageException(
                        "The validated image could not be moved to its final workspace path.");
                }
            }
        }
    }

    private IEnumerable<ImageListItem> EnumerateImages(
        string imagesDirectory,
        CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(imagesDirectory));

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.GetFileSystemInfos("*", options);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException
                    or DirectoryNotFoundException
                    or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Push(childDirectory);
                    continue;
                }

                if (entry is not FileInfo info
                    || !WorkspaceImageFormats.TryFromExtension(
                        info.Extension,
                        out var format))
                {
                    continue;
                }

                ImageListItem? item = null;
                try
                {
                    info.Refresh();
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var relativePath = Path.GetRelativePath(
                        config.KnowledgeBase.Root, info.FullName).Replace('\\', '/');
                    pathGuard.ValidateImagePath(relativePath);
                    item = new ImageListItem(relativePath, format.MimeType,
                        info.Length, new DateTimeOffset(info.LastWriteTimeUtc));
                }
                catch (Exception error) when (
                    error is IOException or UnauthorizedAccessException or KnowledgeBaseAccessException)
                {
                }
                if (item is not null) yield return item;
            }
        }
    }

    private static int FindStartIndex(
        IReadOnlyList<ImageListItem> items,
        string cursorPath)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ComparePaths(items[index].Path, cursorPath) > 0)
            {
                return index;
            }
        }

        return items.Count;
    }

    private static int ComparePaths(string left, string right)
    {
        var insensitive = StringComparer.OrdinalIgnoreCase.Compare(
            left,
            right);
        return insensitive != 0
            ? insensitive
            : StringComparer.Ordinal.Compare(left, right);
    }

    private static async Task<ReadImageResult> ReadImageAsync(
        string absolutePath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var memory = new MemoryStream(
                checked((int)Math.Min(stream.Length, MaxImageBytes)));
            using var hash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            var prefix = new byte[16];
            var prefixLength = 0;
            var buffer = new byte[BufferSize];
            long total = 0;

            while (true)
            {
                var read = await stream.ReadAsync(
                    buffer,
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaxImageBytes)
                {
                    throw new WorkspaceImageException(
                        $"Image '{relativePath}' exceeds the {MaxImageBytes} byte limit.");
                }

                await memory.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
                hash.AppendData(buffer.AsSpan(0, read));
                if (prefixLength < prefix.Length)
                {
                    var copyLength = Math.Min(
                        prefix.Length - prefixLength,
                        read);
                    buffer.AsSpan(0, copyLength)
                        .CopyTo(prefix.AsSpan(prefixLength));
                    prefixLength += copyLength;
                }
            }

            return new ReadImageResult(
                memory.ToArray(),
                prefix[..prefixLength],
                Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant());
        }
        catch (WorkspaceImageException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            throw new WorkspaceImageException(
                $"Image '{relativePath}' could not be read.");
        }
    }

    private void EnsureWritesEnabled()
    {
        if (!config.KnowledgeBase.AllowWrites)
        {
            throw new WorkspaceImageException(
                "Workspace writes are disabled. Set knowledgeBase.allowWrites to true to enable mutation tools.");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record ReadImageResult(
        byte[] Bytes,
        byte[] Prefix,
        string Sha256);
}
