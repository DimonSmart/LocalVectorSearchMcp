using System.Text;
using System.Text.RegularExpressions;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteKnowledgeRepository(SqliteConnectionFactory factory, LocalVectorSearchMcpConfig config) : IKnowledgeRepository, IVectorIndexService, IFullTextSearchService, IIndexManifestService
{
    public const string SchemaVersion = "2";
    private int EffectiveEmbeddingDimensions => config.Embedding.Dimensions ?? 1024;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenVectorConnection();
        await ExecuteAsync(db, "pragma journal_mode = wal;", cancellationToken);
        await CreateSchemaAsync(db, cancellationToken);
    }

    public async Task<bool> HasManifestAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        return (await ScalarLongAsync(db, "select count(*) from index_manifest", [], cancellationToken) ?? 0) > 0;
    }

    public async Task<IndexCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        var indexed = await ReadManifestAsync(db, cancellationToken);
        var problems = CurrentManifest()
            .Where(entry => !indexed.TryGetValue(entry.Key, out var value) || value != entry.Value)
            .Select(entry =>
            {
                var indexedValue = indexed.TryGetValue(entry.Key, out var value) ? $"'{value}'" : "<missing>";
                return $"{entry.Key}: indexed {indexedValue}, current '{entry.Value}'";
            })
            .ToList();
        return new IndexCompatibilityResult(problems.Count == 0, problems);
    }

    public async Task ResetIndexAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenVectorConnection();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(db, """
            drop table if exists chunk_vectors;
            drop table if exists chunks_fts;
            drop table if exists elements;
            drop table if exists chunks;
            drop table if exists documents;
            drop table if exists index_manifest;
            """, cancellationToken, tx: (SqliteTransaction)transaction);
        await CreateSchemaAsync(db, cancellationToken, (SqliteTransaction)transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task WriteCurrentManifestAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        foreach (var entry in CurrentManifest())
        {
            await UpsertManifestAsync(db, entry.Key, entry.Value, cancellationToken);
        }
    }

    private async Task CreateSchemaAsync(SqliteConnection db, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await ExecuteAsync(db, """
            create table if not exists documents (
              id integer primary key,
              path text not null,
              content_hash text not null,
              markdown text not null,
              last_write_time_utc text not null,
              indexed_at_utc text not null,
              unique(path)
            );
            create table if not exists elements (
              id integer primary key,
              document_id integer not null references documents(id) on delete cascade,
              pointer text not null,
              kind text not null,
              text text not null,
              start_line integer not null,
              end_line integer not null,
              heading_path text null,
              ordinal integer not null,
              unique(document_id, pointer)
            );
            create table if not exists chunks (
              id integer primary key,
              document_id integer not null references documents(id) on delete cascade,
              path text not null,
              pointer text not null,
              text text not null,
              heading_path text null,
              embedding_text_hash text not null,
              embedding_model text not null,
              embedding_dimensions integer not null
            );
            create virtual table if not exists chunks_fts using fts5(text, content='chunks', content_rowid='id');
            create table if not exists index_manifest (
              key text primary key,
              value text not null
            );
            """, cancellationToken, tx: transaction);

        try
        {
            await ExecuteAsync(db, $"create virtual table if not exists chunk_vectors using vec0(embedding float[{EffectiveEmbeddingDimensions}]);", cancellationToken, tx: transaction);
        }
        catch (Exception ex)
        {
            throw new VectorIndexException("sqlite-vec initialization failed. Ensure vec0 native extension is available.", ex);
        }

    }

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

    public async Task SaveDocumentIndexAsync(MarkdownSourceDocument document, IReadOnlyList<MarkdownElement> elements, IReadOnlyList<MarkdownChunk> chunks, IReadOnlyList<EmbeddingVector> vectors, CancellationToken cancellationToken)
    {
        if (chunks.Count != vectors.Count) throw new EmbeddingProviderException("Embedding count does not match chunk count.");
        var invalidVector = vectors.FirstOrDefault(vector => vector.Values.Length != EffectiveEmbeddingDimensions);
        if (invalidVector is not null)
        {
            throw new EmbeddingProviderException($"Embedding dimension mismatch. Expected {EffectiveEmbeddingDimensions}, got {invalidVector.Values.Length}.");
        }

        await using var db = OpenVectorConnection();
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        var oldId = await ScalarLongAsync(db, "select id from documents where path = $path", [("$path", document.RelativePath)], cancellationToken);
        if (oldId is not null) await DeleteDocumentByIdAsync(db, (SqliteTransaction)tx, oldId.Value, cancellationToken);

        var insertDoc = db.CreateCommand();
        insertDoc.Transaction = (SqliteTransaction)tx;
        insertDoc.CommandText = "insert into documents(path,content_hash,markdown,last_write_time_utc,indexed_at_utc) values($path,$hash,$md,$lw,$idx); select last_insert_rowid();";
        Add(insertDoc, "$path", document.RelativePath); Add(insertDoc, "$hash", document.ContentHash); Add(insertDoc, "$md", document.Markdown);
        Add(insertDoc, "$lw", document.LastWriteTimeUtc.ToString("O")); Add(insertDoc, "$idx", DateTimeOffset.UtcNow.ToString("O"));
        var documentId = (long)(await insertDoc.ExecuteScalarAsync(cancellationToken))!;

        for (var i = 0; i < elements.Count; i++)
        {
            var e = elements[i];
            var command = db.CreateCommand();
            command.Transaction = (SqliteTransaction)tx;
            command.CommandText = "insert into elements(document_id,pointer,kind,text,start_line,end_line,heading_path,ordinal) values($doc,$ptr,$kind,$text,$start,$end,$heading,$ord)";
            Add(command, "$doc", documentId); Add(command, "$ptr", e.Pointer.Value); Add(command, "$kind", e.Kind.ToString()); Add(command, "$text", e.Text);
            Add(command, "$start", e.StartLine); Add(command, "$end", e.EndLine); Add(command, "$heading", (object?)e.HeadingPath ?? DBNull.Value); Add(command, "$ord", i);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            var vector = vectors[i];
            var insertChunk = db.CreateCommand();
            insertChunk.Transaction = (SqliteTransaction)tx;
            insertChunk.CommandText = "insert into chunks(document_id,path,pointer,text,heading_path,embedding_text_hash,embedding_model,embedding_dimensions) values($doc,$path,$ptr,$text,$heading,$hash,$model,$dim); select last_insert_rowid();";
            Add(insertChunk, "$doc", documentId); Add(insertChunk, "$path", c.Path); Add(insertChunk, "$ptr", c.StartPointer.Value); Add(insertChunk, "$text", c.Text);
            Add(insertChunk, "$heading", (object?)c.HeadingPath ?? DBNull.Value); Add(insertChunk, "$hash", c.EmbeddingTextHash); Add(insertChunk, "$model", config.Embedding.Model); Add(insertChunk, "$dim", vector.Values.Length);
            var chunkId = (long)(await insertChunk.ExecuteScalarAsync(cancellationToken))!;

            var fts = db.CreateCommand();
            fts.Transaction = (SqliteTransaction)tx;
            fts.CommandText = "insert into chunks_fts(rowid, text) values($id, $text)";
            Add(fts, "$id", chunkId); Add(fts, "$text", c.Text);
            await fts.ExecuteNonQueryAsync(cancellationToken);

            var vec = db.CreateCommand();
            vec.Transaction = (SqliteTransaction)tx;
            vec.CommandText = "insert into chunk_vectors(rowid, embedding) values($id, $embedding)";
            Add(vec, "$id", chunkId); Add(vec, "$embedding", ToVectorJson(vector.Values));
            await vec.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<int> DeleteMissingDocumentsAsync(IReadOnlySet<string> currentRelativePaths, CancellationToken cancellationToken)
    {
        await using var db = OpenVectorConnection();
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

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var deleted = 0;
        foreach (var document in documents.Where(document => !currentRelativePaths.Contains(document.Path)))
        {
            await DeleteDocumentByIdAsync(db, (SqliteTransaction)transaction, document.Id, cancellationToken);
            deleted++;
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(EmbeddingVector queryEmbedding, int topK, CancellationToken cancellationToken)
    {
        await using var db = OpenVectorConnection();
        var command = db.CreateCommand();
        command.CommandText = """
            select v.rowid, v.distance
            from chunk_vectors v
            where v.embedding match $embedding and k = $top
            order by v.distance
            """;
        Add(command, "$embedding", ToVectorJson(queryEmbedding.Values)); Add(command, "$top", topK);
        try
        {
            var list = new List<SemanticSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) list.Add(new SemanticSearchResult(reader.GetInt64(0), reader.GetDouble(1)));
            return list;
        }
        catch (Exception ex)
        {
            throw new VectorIndexException("Vector search failed.", ex);
        }
    }

    public async Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(string query, int topK, CancellationToken cancellationToken)
    {
        var ftsQuery = FtsQuery(query);
        if (ftsQuery.Length == 0) return [];
        await using var db = factory.Open();
        var command = db.CreateCommand();
        command.CommandText = """
            select f.rowid, bm25(chunks_fts) as score, snippet(chunks_fts, 0, '[', ']', '...', 16) as snippet
            from chunks_fts f
            where chunks_fts match $query
            order by score
            limit $top
            """;
        Add(command, "$query", ftsQuery); Add(command, "$top", topK);
        try
        {
            var list = new List<LexicalSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) list.Add(new LexicalSearchResult(reader.GetInt64(0), reader.GetDouble(1), reader.GetString(2)));
            return list;
        }
        catch (Exception ex)
        {
            throw new FullTextSearchException("Full text search failed.", ex);
        }
    }

    public async Task<IReadOnlyList<ChunkSearchDocument>> GetChunksAsync(IReadOnlyCollection<long> chunkIds, CancellationToken cancellationToken)
    {
        if (chunkIds.Count == 0) return [];
        await using var db = factory.Open();
        var ids = string.Join(",", chunkIds.Select((_, i) => "$id" + i));
        var command = db.CreateCommand();
        command.CommandText = $"select id, path, pointer, text, heading_path from chunks where id in ({ids})";
        var i = 0;
        foreach (var id in chunkIds) Add(command, "$id" + i++, id);
        var list = new List<ChunkSearchDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ChunkSearchDocument(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return list;
    }

    public async Task<MarkdownSlice> ReadSliceAsync(string path, SemanticPointer pointer, int maxElements, int maxBytes, CancellationToken cancellationToken)
    {
        maxElements = Math.Clamp(maxElements, 1, 100);
        maxBytes = Math.Clamp(maxBytes, 1, 100_000);
        await using var db = factory.Open();
        var command = db.CreateCommand();
        command.CommandText = """
            select e.pointer, e.kind, e.text, e.heading_path
            from elements e
            join documents d on d.id = e.document_id
            where d.path = $path
              and e.ordinal >= (select ordinal from elements e2 where e2.document_id = d.id and e2.pointer = $ptr)
            order by e.ordinal
            limit $max
            """;
        Add(command, "$path", path); Add(command, "$ptr", pointer.Value); Add(command, "$max", maxElements + 1);
        var elements = new List<MarkdownSliceElement>();
        string? nextPointer = null;
        var bytes = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var currentPointer = reader.GetString(0);
            var text = reader.GetString(2);
            if (elements.Count >= maxElements)
            {
                nextPointer = currentPointer;
                break;
            }

            var separatorBytes = elements.Count == 0 ? 0 : Encoding.UTF8.GetByteCount("\n\n");
            var projectedBytes = bytes + separatorBytes + Encoding.UTF8.GetByteCount(text);
            if (elements.Count > 0 && projectedBytes > maxBytes)
            {
                nextPointer = currentPointer;
                break;
            }

            elements.Add(new MarkdownSliceElement(currentPointer, Enum.Parse<MarkdownElementKind>(reader.GetString(1)), text, reader.IsDBNull(3) ? null : reader.GetString(3)));
            bytes = projectedBytes;
        }

        if (elements.Count == 0) throw new SemanticPointerNotFoundException($"Pointer '{pointer.Value}' was not found in '{path}'.");
        var markdown = string.Join("\n\n", elements.Select(e => e.Text));
        return new MarkdownSlice(path, pointer.Value, elements, markdown, nextPointer);
    }

    public async Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        var docs = await ScalarLongAsync(db, "select count(*) from documents", [], cancellationToken) ?? 0;
        var chunks = await ScalarLongAsync(db, "select count(*) from chunks", [], cancellationToken) ?? 0;
        var last = await ScalarStringAsync(db, "select max(indexed_at_utc) from documents", [], cancellationToken);
        var project = new ProjectIndexStatus(config.KnowledgeBase.Root, (int)docs, (int)chunks, DateTimeOffset.TryParse(last, out var dto) ? dto : null);
        return new StatusResponse(config.Storage.Path, SchemaVersion, MarkdownChunker.Version, EmbeddingTextBuilder.Version, config.Embedding.Model, EffectiveEmbeddingDimensions, project);
    }

    public async Task<bool> HasChunksAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        var count = await ScalarLongAsync(db, "select count(*) from chunks", [], cancellationToken) ?? 0;
        return count > 0;
    }

    private static async Task DeleteDocumentByIdAsync(SqliteConnection db, SqliteTransaction? tx, long documentId, CancellationToken cancellationToken)
    {
        var chunkIds = new List<long>();
        var get = db.CreateCommand();
        get.Transaction = tx;
        get.CommandText = "select id from chunks where document_id = $doc";
        Add(get, "$doc", documentId);
        await using (var reader = await get.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) chunkIds.Add(reader.GetInt64(0));
        }

        foreach (var id in chunkIds)
        {
            await ExecuteAsync(db, "delete from chunks_fts where rowid = $id", cancellationToken, [("$id", id)], tx);
            await ExecuteAsync(db, "delete from chunk_vectors where rowid = $id", cancellationToken, [("$id", id)], tx);
        }

        await ExecuteAsync(db, "delete from documents where id = $doc", cancellationToken, [("$doc", documentId)], tx);
    }

    private SqliteConnection OpenVectorConnection()
    {
        var db = factory.Open();
        try
        {
            db.EnableExtensions();
            db.LoadVector();
            return db;
        }
        catch (Exception exception)
        {
            db.Dispose();
            throw new VectorIndexException("sqlite-vec initialization failed. Ensure vec0 native extension is available.", exception);
        }
    }

    private static string ToVectorJson(float[] values) => "[" + string.Join(",", values.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

    private static string FtsQuery(string query)
    {
        var terms = Regex.Matches(query, @"[\p{L}\p{N}_-]+").Select(m => "\"" + m.Value.Replace("\"", "\"\"") + "\"").ToList();
        return string.Join(" AND ", terms);
    }

    private static async Task UpsertManifestAsync(SqliteConnection db, string key, string value, CancellationToken cancellationToken)
        => await ExecuteAsync(db, "insert into index_manifest(key,value) values($key,$value) on conflict(key) do update set value = excluded.value", cancellationToken, [("$key", key), ("$value", value)]);

    private IReadOnlyDictionary<string, string> CurrentManifest() => new Dictionary<string, string>
    {
        ["schema_version"] = SchemaVersion,
        ["chunker_version"] = MarkdownChunker.Version,
        ["embedding_text_builder_version"] = EmbeddingTextBuilder.Version,
        ["embedding_model"] = config.Embedding.Model,
        ["embedding_dimensions"] = EffectiveEmbeddingDimensions.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static async Task<IReadOnlyDictionary<string, string>> ReadManifestAsync(SqliteConnection db, CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
        command.CommandText = "select key, value from index_manifest";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private static async Task ExecuteAsync(SqliteConnection db, string sql, CancellationToken cancellationToken, IReadOnlyList<(string Name, object? Value)>? parameters = null, SqliteTransaction? tx = null)
    {
        var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        if (parameters is not null) foreach (var p in parameters) Add(command, p.Name, p.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> ScalarLongAsync(SqliteConnection db, string sql, IReadOnlyList<(string Name, object? Value)> parameters, CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) Add(command, p.Name, p.Value ?? DBNull.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<string?> ScalarStringAsync(SqliteConnection db, string sql, IReadOnlyList<(string Name, object? Value)> parameters, CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) Add(command, p.Name, p.Value ?? DBNull.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
}
