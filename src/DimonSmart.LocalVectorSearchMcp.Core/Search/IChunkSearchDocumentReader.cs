namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public interface IChunkSearchDocumentReader
{
    Task<IReadOnlyList<ChunkSearchDocument>> GetChunksAsync(
        IReadOnlyCollection<long> chunkIds,
        CancellationToken cancellationToken);
}
