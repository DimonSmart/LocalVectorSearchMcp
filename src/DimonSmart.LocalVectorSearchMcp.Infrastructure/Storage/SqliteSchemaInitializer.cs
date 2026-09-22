using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteSchemaInitializer(SqliteConnectionFactory factory, LocalVectorSearchMcpConfig config) : IIndexInitializer
{
    private int EffectiveEmbeddingDimensions => config.Embedding.Dimensions ?? 1024;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        SqliteVectorExtensionLoader.Load(db);
        await db.ExecuteAsync("pragma journal_mode = wal;", cancellationToken);
        await CreateSchemaAsync(db, cancellationToken);
        await EnsureCurrentElementsSchemaAsync(db, cancellationToken);
    }

    private async Task EnsureCurrentElementsSchemaAsync(
        SqliteConnection db,
        CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
        command.CommandText = "pragma table_info(elements)";
        var hasColumns = false;
        var hasSelfHash = false;
        var hasSubtreeHash = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                hasColumns = true;
                var name = reader.GetString(1);
                hasSelfHash |= name.Equals("self_hash", StringComparison.OrdinalIgnoreCase);
                hasSubtreeHash |= name.Equals("subtree_hash", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!hasColumns || (hasSelfHash && hasSubtreeHash))
        {
            return;
        }

        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken);
        await db.ExecuteAsync("""
            drop table if exists chunk_vectors;
            drop table if exists chunks_fts;
            drop table if exists elements;
            drop table if exists chunks;
            drop table if exists documents;
            drop table if exists index_manifest;
            """, cancellationToken, transaction: transaction);
        await CreateSchemaAsync(db, cancellationToken, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    internal async Task CreateSchemaAsync(
        SqliteConnection db,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await db.ExecuteAsync("""
            create table if not exists documents (
              id integer primary key,
              path text not null,
              content_hash text not null,
              source_hash text not null,
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
              source_start integer not null,
              source_length integer not null,
              self_hash text null,
              subtree_hash text null,
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
            create virtual table if not exists chunks_fts using fts5(text, heading_path);
            create table if not exists index_manifest (
              key text primary key,
              value text not null
            );
            """, cancellationToken, transaction: transaction);

        try
        {
            await db.ExecuteAsync(
                $"create virtual table if not exists chunk_vectors using vec0(embedding float[{EffectiveEmbeddingDimensions}]);",
                cancellationToken,
                transaction: transaction);
        }
        catch (Exception ex) when (ex is not VectorIndexException)
        {
            throw new VectorIndexException("sqlite-vec initialization failed. Ensure vec0 native extension is available.", ex);
        }
    }
}
