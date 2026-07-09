namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

public interface IIndexManifestService
{
    Task<bool> HasManifestAsync(CancellationToken cancellationToken);
    Task<IndexCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken);
    Task ResetIndexAsync(CancellationToken cancellationToken);
    Task WriteCurrentManifestAsync(CancellationToken cancellationToken);
}
