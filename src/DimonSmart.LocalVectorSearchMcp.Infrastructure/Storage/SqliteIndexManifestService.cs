using System.Globalization;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteIndexManifestService(
    SqliteConnectionFactory factory,
    LocalVectorSearchMcpConfig config,
    SqliteSchemaInitializer schemaInitializer) : IIndexManifestService
{
    private int EffectiveEmbeddingDimensions => config.Embedding.Dimensions ?? 1024;

    public async Task<bool> HasManifestAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        return (await db.ScalarLongAsync("select count(*) from index_manifest", [], cancellationToken) ?? 0) > 0;
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
        await using var db = factory.Open();
        SqliteVectorExtensionLoader.Load(db);
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken);
        await db.ExecuteAsync("""
            drop table if exists chunk_vectors;
            drop table if exists chunks_fts;
            drop table if exists elements;
            drop table if exists chunks;
            drop table if exists documents;
            drop table if exists index_manifest;
            """, cancellationToken, transaction: transaction);
        await schemaInitializer.CreateSchemaAsync(db, cancellationToken, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task WriteCurrentManifestAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        foreach (var entry in CurrentManifest())
        {
            await db.ExecuteAsync(
                "insert into index_manifest(key,value) values($key,$value) on conflict(key) do update set value = excluded.value",
                cancellationToken,
                [("$key", entry.Key), ("$value", entry.Value)]);
        }
    }

    private IReadOnlyDictionary<string, string> CurrentManifest() => new Dictionary<string, string>
    {
        ["schema_version"] = SqliteSchema.Version,
        ["chunker_version"] = MarkdownChunker.Version,
        ["embedding_text_builder_version"] = EmbeddingTextBuilder.Version,
        ["embedding_model"] = config.Embedding.Model,
        ["embedding_dimensions"] = EffectiveEmbeddingDimensions.ToString(CultureInfo.InvariantCulture),
        ["chunking_max_chunk_bytes"] = config.Chunking.MaxChunkBytes.ToString(CultureInfo.InvariantCulture),
        ["chunking_max_elements"] = config.Chunking.MaxElements.ToString(CultureInfo.InvariantCulture),
        ["chunking_include_heading_context"] = config.Chunking.IncludeHeadingContext.ToString().ToLowerInvariant(),
        ["chunking_include_front_matter"] = config.Chunking.IncludeFrontMatter.ToString().ToLowerInvariant()
    };

    private static async Task<IReadOnlyDictionary<string, string>> ReadManifestAsync(
        SqliteConnection db,
        CancellationToken cancellationToken)
    {
        var command = db.CreateCommand();
        command.CommandText = "select key, value from index_manifest";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }
}
