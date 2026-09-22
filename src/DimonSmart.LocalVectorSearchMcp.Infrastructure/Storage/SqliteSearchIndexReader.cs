using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteSearchIndexReader(SqliteConnectionFactory factory) :
    IChunkSearchDocumentReader,
    ISearchIndexStateReader
{
    public async Task<IReadOnlyList<ChunkSearchDocument>> GetChunksAsync(
        IReadOnlyCollection<long> chunkIds,
        CancellationToken cancellationToken)
    {
        if (chunkIds.Count == 0) return [];

        await using var db = factory.Open();
        var placeholders = string.Join(",", chunkIds.Select((_, index) => "$id" + index));
        var command = db.CreateCommand();
        command.CommandText = $"""
            select c.id, c.path, c.pointer, c.text, c.heading_path, e.text, e.self_hash, e.subtree_hash
            from chunks c
            join elements e
              on e.document_id = c.document_id
             and e.pointer = c.pointer
            where c.id in ({placeholders})
            """;
        var index = 0;
        foreach (var chunkId in chunkIds) command.AddParameter("$id" + index++, chunkId);

        var result = new List<ChunkSearchDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ChunkSearchDocument(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return result;
    }

    public async Task<bool> HasChunksAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        return (await db.ScalarLongAsync("select count(*) from chunks", [], cancellationToken) ?? 0) > 0;
    }
}
