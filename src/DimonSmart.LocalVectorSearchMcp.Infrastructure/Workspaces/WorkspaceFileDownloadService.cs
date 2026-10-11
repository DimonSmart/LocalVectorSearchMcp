using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class WorkspaceFileDownloadService(
    KnowledgeBasePathGuard pathGuard) : IWorkspaceFileDownloadService
{
    private const long MaxDownloadBytes = 25L * 1024 * 1024;

    public async Task<DownloadedWorkspaceFile> DownloadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var normalized = pathGuard.ValidateDownloadPath(path);
        var absolute = pathGuard.ResolveDownloadPath(normalized);
        var file = new FileInfo(absolute);
        if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0)
            throw new WorkspaceFileNotFoundException(normalized);
        if (file.Length > MaxDownloadBytes)
            throw new WorkspaceMutationException(
                $"File '{normalized}' exceeds the 25 MiB download limit.");

        var data = await File.ReadAllBytesAsync(absolute, cancellationToken);
        if (data.LongLength > MaxDownloadBytes)
            throw new WorkspaceMutationException(
                $"File '{normalized}' exceeds the 25 MiB download limit.");

        return new DownloadedWorkspaceFile(
            normalized,
            MimeTypeFor(normalized),
            data.LongLength,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            data);
    }

    private static string MimeTypeFor(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".md" or ".txt" or ".log" => "text/plain",
            ".json" => "application/json",
            ".yml" or ".yaml" => "application/yaml",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
}
