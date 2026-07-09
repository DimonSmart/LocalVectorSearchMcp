using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteDocumentIndexStore(
    SqliteConnectionFactory factory,
    LocalVectorSearchMcpConfig config,
    SqliteDocumentDeletionService deletionService) : IDocumentIndexStore
{
    private int EffectiveEmbeddingDimensions => config.Embedding.Dimensions ?? 1024;

    public async Task<IReadOnlyDictionary<string, string>> GetDocumentHashesAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        var command = db.CreateCommand();
        command.CommandText = "select path, content_hash from documents";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public async Task SaveDocumentIndexAsync(
        MarkdownSourceDocument document,
        IReadOnlyList<MarkdownElement> elements,
        IReadOnlyList<MarkdownChunk> chunks,
        IReadOnlyList<EmbeddingVector> vectors,
        CancellationToken cancellationToken)
    {
        if (chunks.Count != vectors.Count)
        {
            throw new EmbeddingProviderException("Embedding count does not match chunk count.");
        }

        var invalidVector = vectors.FirstOrDefault(vector => vector.Values.Length != EffectiveEmbeddingDimensions);
        if (invalidVector is not null)
        {
            throw new EmbeddingProviderException(
                $"Embedding dimension mismatch. Expected {EffectiveEmbeddingDimensions}, got {invalidVector.Values.Length}.");
        }

        await using var db = factory.Open();
        SqliteVectorExtensionLoader.Load(db);
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken);
        var oldId = await db.ScalarLongAsync(
            "select id from documents where path = $path",
            [("$path", document.RelativePath)],
            cancellationToken,
            transaction);
        if (oldId is not null)
        {
            await deletionService.DeleteDocumentByIdAsync(db, oldId.Value, cancellationToken, transaction);
        }

        var insertDocument = db.CreateCommand();
        insertDocument.Transaction = transaction;
        insertDocument.CommandText = "insert into documents(path,content_hash,markdown,last_write_time_utc,indexed_at_utc) values($path,$hash,$md,$lw,$idx); select last_insert_rowid();";
        insertDocument.AddParameter("$path", document.RelativePath);
        insertDocument.AddParameter("$hash", document.ContentHash);
        insertDocument.AddParameter("$md", document.Markdown);
        insertDocument.AddParameter("$lw", document.LastWriteTimeUtc.ToString("O"));
        insertDocument.AddParameter("$idx", DateTimeOffset.UtcNow.ToString("O"));
        var documentId = (long)(await insertDocument.ExecuteScalarAsync(cancellationToken))!;

        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "insert into elements(document_id,pointer,kind,text,start_line,end_line,heading_path,ordinal) values($doc,$ptr,$kind,$text,$start,$end,$heading,$ord)";
            command.AddParameter("$doc", documentId);
            command.AddParameter("$ptr", element.Pointer.Value);
            command.AddParameter("$kind", element.Kind.ToString());
            command.AddParameter("$text", element.Text);
            command.AddParameter("$start", element.StartLine);
            command.AddParameter("$end", element.EndLine);
            command.AddParameter("$heading", element.HeadingPath);
            command.AddParameter("$ord", index);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var vector = vectors[index];
            var insertChunk = db.CreateCommand();
            insertChunk.Transaction = transaction;
            insertChunk.CommandText = "insert into chunks(document_id,path,pointer,text,heading_path,embedding_text_hash,embedding_model,embedding_dimensions) values($doc,$path,$ptr,$text,$heading,$hash,$model,$dim); select last_insert_rowid();";
            insertChunk.AddParameter("$doc", documentId);
            insertChunk.AddParameter("$path", chunk.Path);
            insertChunk.AddParameter("$ptr", chunk.StartPointer.Value);
            insertChunk.AddParameter("$text", chunk.Text);
            insertChunk.AddParameter("$heading", chunk.HeadingPath);
            insertChunk.AddParameter("$hash", chunk.EmbeddingTextHash);
            insertChunk.AddParameter("$model", config.Embedding.Model);
            insertChunk.AddParameter("$dim", vector.Values.Length);
            var chunkId = (long)(await insertChunk.ExecuteScalarAsync(cancellationToken))!;

            var fts = db.CreateCommand();
            fts.Transaction = transaction;
            fts.CommandText = "insert into chunks_fts(rowid, text) values($id, $text)";
            fts.AddParameter("$id", chunkId);
            fts.AddParameter("$text", chunk.Text);
            await fts.ExecuteNonQueryAsync(cancellationToken);

            var vectorCommand = db.CreateCommand();
            vectorCommand.Transaction = transaction;
            vectorCommand.CommandText = "insert into chunk_vectors(rowid, embedding) values($id, $embedding)";
            vectorCommand.AddParameter("$id", chunkId);
            vectorCommand.AddParameter("$embedding", SqliteVectorSerializer.ToJson(vector.Values));
            await vectorCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> DeleteMissingDocumentsAsync(
        IReadOnlySet<string> currentRelativePaths,
        CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        SqliteVectorExtensionLoader.Load(db);
        var documents = new List<(long Id, string Path)>();
        var command = db.CreateCommand();
        command.CommandText = "select id, path from documents";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                documents.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken);
        var deleted = 0;
        foreach (var document in documents.Where(document => !currentRelativePaths.Contains(document.Path)))
        {
            await deletionService.DeleteDocumentByIdAsync(db, document.Id, cancellationToken, transaction);
            deleted++;
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }
}
