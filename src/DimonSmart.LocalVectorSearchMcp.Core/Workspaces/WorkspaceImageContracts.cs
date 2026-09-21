namespace DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

public sealed record SaveImageRequest(
    string DownloadUrl,
    string? DeclaredMimeType,
    string? SourceFileName,
    string? FileName,
    string? AltText);

public sealed record ImageSaveResponse(
    string Path,
    string MimeType,
    long Bytes,
    string Sha256,
    string Markdown);

public sealed record ImageListItem(
    string Path,
    string MimeType,
    long Bytes,
    DateTimeOffset LastWriteTimeUtc);

public sealed record ImageListResponse(
    IReadOnlyList<ImageListItem> Images,
    string? NextCursor);

public sealed record ImageLoadResponse(
    string Path,
    string MimeType,
    long Bytes,
    string Sha256);

public sealed record LoadedImage(
    ImageLoadResponse Metadata,
    byte[] Data);

public sealed record ImageDeleteResponse(
    string Path,
    bool Deleted);

public sealed record RemoteFileDownloadResult(
    long Bytes,
    string Sha256,
    byte[] Prefix,
    string? ContentType);

public interface IRemoteFileDownloader
{
    Task<RemoteFileDownloadResult> DownloadAsync(
        string downloadUrl,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken);
}

public interface IWorkspaceImageService
{
    Task<ImageSaveResponse> SaveAsync(
        SaveImageRequest request,
        CancellationToken cancellationToken);

    Task<ImageListResponse> ListAsync(
        string? cursor,
        int? pageSize,
        CancellationToken cancellationToken);

    Task<LoadedImage> LoadAsync(
        string path,
        CancellationToken cancellationToken);

    Task<ImageDeleteResponse> DeleteAsync(
        string path,
        CancellationToken cancellationToken);
}

public sealed class WorkspaceImageException(string message) : Exception(message);
