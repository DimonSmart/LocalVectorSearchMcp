using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteDocumentDeletionService
{
    public async Task DeleteDocumentByIdAsync(
        SqliteConnection db,
        long documentId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        var chunkIds = new List<long>();
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select id from chunks where document_id = $doc";
        command.AddParameter("$doc", documentId);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) chunkIds.Add(reader.GetInt64(0));
        }

        foreach (var chunkId in chunkIds)
        {
            await db.ExecuteAsync(
                "delete from chunks_fts where rowid = $id",
                cancellationToken,
                [("$id", chunkId)],
                transaction);
            await db.ExecuteAsync(
                "delete from chunk_vectors where rowid = $id",
                cancellationToken,
                [("$id", chunkId)],
                transaction);
        }

        await db.ExecuteAsync(
            "delete from documents where id = $doc",
            cancellationToken,
            [("$doc", documentId)],
            transaction);
    }
}
