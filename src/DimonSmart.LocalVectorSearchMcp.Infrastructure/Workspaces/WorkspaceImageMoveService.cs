using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

/// <summary>Moves only binary assets. Markdown links remain client-managed text.</summary>
public sealed class WorkspaceImageMoveService : IWorkspaceImageMoveService
{
    private readonly LocalVectorSearchMcpConfig config;
    private readonly KnowledgeBasePathGuard pathGuard;
    private readonly IWorkspaceImageMoveFileOperations files;

    public WorkspaceImageMoveService(LocalVectorSearchMcpConfig config, KnowledgeBasePathGuard pathGuard)
        : this(config, pathGuard, PhysicalWorkspaceImageMoveFileOperations.Instance) { }

    internal WorkspaceImageMoveService(
        LocalVectorSearchMcpConfig config, KnowledgeBasePathGuard pathGuard,
        IWorkspaceImageMoveFileOperations files)
    {
        this.config = config;
        this.pathGuard = pathGuard;
        this.files = files;
    }

    public Task<ImageMoveResponse> MoveAsync(MoveImageRequest request, CancellationToken cancellationToken)
        => WorkspaceMutationGate.RunAsync(() => MoveCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ImageMoveResponse> MoveCoreAsync(
        MoveImageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!config.KnowledgeBase.AllowWrites)
            throw new WorkspaceImageException("Workspace writes are disabled. Set knowledgeBase.allowWrites to true.");
        if (request.UpdateReferences == true)
            throw new WorkspaceImageException("updateReferences=true is no longer supported. Move the image, then update Markdown links using kb_patch.");

        var source = pathGuard.ValidateImagePath(request.SourcePath);
        var target = pathGuard.ValidateImagePath(request.TargetPath);
        var sourceAbsolute = pathGuard.ResolveImagePath(source);
        var targetAbsolute = pathGuard.ResolveImagePath(target);
        if (string.Equals(sourceAbsolute, targetAbsolute,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new WorkspaceImageException("Source and target image paths must be different.");

        if (!WorkspaceImageFormats.TryFromExtension(Path.GetExtension(source), out var sourceFormat)
            || !WorkspaceImageFormats.TryFromExtension(Path.GetExtension(target), out var targetFormat))
            throw new WorkspaceImageException("Source and target must have supported image extensions.");

        if (request.ExpectedSha256 is not null
            && (request.ExpectedSha256.Length != 64
                || !request.ExpectedSha256.All(Uri.IsHexDigit)))
            throw new WorkspaceImageException("expectedSha256 must contain exactly 64 hexadecimal characters.");

        ValidateSource(sourceAbsolute, source);
        ValidateTarget(targetAbsolute, target);
        var state = await ReadStateAsync(sourceAbsolute, source, cancellationToken);
        var detected = WorkspaceImageFormats.Detect(state.Prefix)
            ?? throw new WorkspaceImageException("Source image has an invalid or unsupported signature.");
        if (detected.Format != sourceFormat.Format || detected.Format != targetFormat.Format)
            throw new WorkspaceImageException("Image signature does not match source or target extension.");
        if (request.ExpectedSha256 is not null
            && !string.Equals(request.ExpectedSha256, state.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceImageException("Image SHA-256 differs from expectedSha256.");

        var created = new List<string>();
        try
        {
            CreateDirectories(target, created);
            cancellationToken.ThrowIfCancellationRequested();
            sourceAbsolute = pathGuard.ResolveImagePath(source);
            targetAbsolute = pathGuard.ResolveImagePath(target);
            ValidateSource(sourceAbsolute, source);
            ValidateTarget(targetAbsolute, target);
            var final = await ReadStateAsync(sourceAbsolute, source, cancellationToken);
            if (!string.Equals(state.Sha256, final.Sha256, StringComparison.Ordinal))
                throw new WorkspaceImageException("Source image changed during the move.");

            try
            {
                files.Move(sourceAbsolute, targetAbsolute, overwrite: false);
            }
            catch (IOException)
            {
                throw new WorkspaceImageException("Image move failed: destination may exist, or an atomic no-overwrite move is unavailable.");
            }
            catch (UnauthorizedAccessException)
            {
                throw new WorkspaceImageException("Image move was denied by the filesystem.");
            }

            return new ImageMoveResponse(source, target, state.Sha256);
        }
        finally
        {
            foreach (var directory in created.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        files.DeleteDirectory(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void CreateDirectories(string target, List<string> created)
    {
        var pieces = target.Split('/');
        var relative = "";
        for (var index = 0; index < pieces.Length - 1; index++)
        {
            relative = relative.Length == 0 ? pieces[index] : relative + "/" + pieces[index];
            var absolute = pathGuard.ResolveImageDirectory(relative);
            if (Directory.Exists(absolute)) continue;
            try
            {
                files.CreateDirectory(absolute);
                created.Add(absolute);
            }
            catch (IOException) when (Directory.Exists(absolute)) { }
            // Check again even when a concurrent process created the directory.
            _ = pathGuard.ResolveImageDirectory(relative);
        }
    }

    private static void ValidateSource(string absolute, string relative)
    {
        if (Directory.Exists(absolute))
            throw new WorkspaceImageException($"Image '{relative}' is a directory.");
        if (!File.Exists(absolute))
            throw new WorkspaceImageException($"Image '{relative}' does not exist.");
    }

    private static void ValidateTarget(string absolute, string relative)
    {
        // Reparse points (including dangling links) are rejected by the path guard.
        if (File.Exists(absolute) || Directory.Exists(absolute))
            throw new WorkspaceImageException($"Destination '{relative}' already exists.");
    }

    private static async Task<ImageState> ReadStateAsync(
        string absolute, string relative, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(absolute, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var prefix = new byte[16];
            var length = await stream.ReadAsync(prefix, cancellationToken);
            stream.Position = 0;
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            return new ImageState(prefix[..length], sha);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceImageException($"Image '{relative}' could not be read safely.");
        }
    }

    private sealed record ImageState(byte[] Prefix, string Sha256);
}

internal interface IWorkspaceImageMoveFileOperations
{
    void Move(string sourcePath, string destinationPath, bool overwrite);
    void CreateDirectory(string path);
    void DeleteDirectory(string path);
}

internal sealed class PhysicalWorkspaceImageMoveFileOperations : IWorkspaceImageMoveFileOperations
{
    public static PhysicalWorkspaceImageMoveFileOperations Instance { get; } = new();
    public void Move(string sourcePath, string destinationPath, bool overwrite)
        => File.Move(sourcePath, destinationPath, overwrite);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void DeleteDirectory(string path) => Directory.Delete(path);
}
