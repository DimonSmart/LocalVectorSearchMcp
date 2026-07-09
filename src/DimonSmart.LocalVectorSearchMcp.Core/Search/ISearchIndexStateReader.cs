namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public interface ISearchIndexStateReader
{
    Task<bool> HasChunksAsync(CancellationToken cancellationToken);
}
