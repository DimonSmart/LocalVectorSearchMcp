using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class WorkspaceImageMoveService(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard) : IWorkspaceImageMoveService
{
    public Task<ImageMoveResponse> MoveAsync(
        MoveImageRequest request,
        CancellationToken cancellationToken)
        => WorkspaceMutationGate.RunAsync(
            () => MoveCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task<ImageMoveResponse> MoveCoreAsync(
        MoveImageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!config.KnowledgeBase.AllowWrites)
        {
            throw new WorkspaceImageException(
                "Workspace writes are disabled.", "PERMISSION_DENIED");
        }

        if (request.UpdateReferences is true)
        {
            throw new WorkspaceImageException(
                "updateReferences=true is no longer supported; update links using kb_patch.");
        }

        if (request.ExpectedSha256 is not null
            && (request.ExpectedSha256.Length != 64
                || !request.ExpectedSha256.All(Uri.IsHexDigit)))
        {
            throw new WorkspaceImageException(
                "expectedSha256 must be 64 hexadecimal characters.");
        }

        var sourcePath = pathGuard.ValidateImagePath(request.SourcePath);
        var targetPath = pathGuard.ValidateImagePath(request.TargetPath);
        var source = pathGuard.ResolveImagePath(sourcePath);
        var target = pathGuard.ResolveImagePath(targetPath);
        if (source.Equals(target, PathComparison()))
        {
            throw new WorkspaceImageException(
                "Source and target must be different files.");
        }

        if (!File.Exists(source))
        {
            throw new WorkspaceImageException(
                "Source image does not exist.", "NOT_FOUND");
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new WorkspaceImageException(
                "Destination already exists.", "ALREADY_EXISTS");
        }

        if (!WorkspaceImageFormats.TryFromExtension(
                Path.GetExtension(sourcePath), out var sourceFormat)
            || !WorkspaceImageFormats.TryFromExtension(
                Path.GetExtension(targetPath), out var targetFormat))
        {
            throw new WorkspaceImageException(
                "Unsupported image file extension.", "UNSUPPORTED_FORMAT");
        }

        var (hash, detected) = await ReadImageStateAsync(
            source, cancellationToken);
        if (detected is null
            || detected.Format != sourceFormat.Format
            || detected.Format != targetFormat.Format)
        {
            throw new WorkspaceImageException(
                "Image signature does not match its extensions.",
                "UNSUPPORTED_FORMAT");
        }

        if (request.ExpectedSha256 is not null
            && !hash.Equals(
                request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceImageException(
                "Image changed since it was read.", "CONFLICT");
        }

        var created = pathGuard.CreateImageParentDirectories(targetPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            source = pathGuard.ResolveImagePath(sourcePath);
            target = pathGuard.ResolveImagePath(targetPath);
            if (File.Exists(target) || Directory.Exists(target))
            {
                throw new WorkspaceImageException(
                    "Destination already exists.", "ALREADY_EXISTS");
            }

            var (currentHash, currentFormat) = await ReadImageStateAsync(
                source, cancellationToken);
            if (currentHash != hash
                || currentFormat?.Format != detected.Format)
            {
                throw new WorkspaceImageException(
                    "Image changed during move.", "CONFLICT");
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(source, target, overwrite: false);
            }
            catch (IOException)
            {
                if (File.Exists(target) && File.Exists(source))
                {
                    throw new WorkspaceImageException(
                        "Destination already exists.", "ALREADY_EXISTS");
                }

                if (!File.Exists(source) || File.Exists(target))
                {
                    throw new WorkspaceImageException(
                        "Move outcome is uncertain; inspect both paths before retrying.",
                        "PARTIAL_FAILURE");
                }

                throw new WorkspaceImageException(
                    "Image move failed.", "INTERNAL_ERROR");
            }
            catch (UnauthorizedAccessException)
            {
                throw new WorkspaceImageException(
                    "Permission denied while moving image.", "PERMISSION_DENIED");
            }

            return new ImageMoveResponse(sourcePath, targetPath, hash);
        }
        finally
        {
            foreach (var directory in created.Reverse())
            {
                KnowledgeBasePathGuard.TryRemoveEmptyDirectory(directory);
            }
        }
    }

    private static async Task<(string Hash, WorkspaceImageFormatInfo? Format)>
        ReadImageStateAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > WorkspaceImageService.MaxImageBytes)
            {
                throw new WorkspaceImageException(
                    "Image exceeds the 25 MiB limit.");
            }

            var prefix = new byte[16];
            var length = await stream.ReadAsync(prefix, cancellationToken);
            stream.Position = 0;
            var sha = await SHA256.HashDataAsync(stream, cancellationToken);
            return (Convert.ToHexString(sha).ToLowerInvariant(),
                WorkspaceImageFormats.Detect(prefix.AsSpan(0, length)));
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new WorkspaceImageException(
                "Source image disappeared during move.", "CONFLICT");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceImageException(
                "Source image cannot be read safely.", "PERMISSION_DENIED");
        }
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
