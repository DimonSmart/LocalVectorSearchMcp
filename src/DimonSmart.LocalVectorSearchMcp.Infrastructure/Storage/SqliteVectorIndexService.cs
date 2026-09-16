using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteVectorIndexService(SqliteConnectionFactory factory) : IVectorIndexService
{
    public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        EmbeddingVector queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
        => SearchAsync(queryEmbedding, topK, null, cancellationToken);

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        EmbeddingVector queryEmbedding,
        int topK,
        SearchPathScope? scope,
        CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        SqliteVectorExtensionLoader.Load(db);
        if (scope is not null
            && !await ScopedSearchFilter.PrepareAsync(db, scope, cancellationToken))
        {
            return [];
        }

        var command = db.CreateCommand();
        command.CommandText = scope is null
            ? """
                select v.rowid, v.distance
                from chunk_vectors v
                where v.embedding match $embedding and k = $top
                order by v.distance
                """
            : $"""
                select v.rowid, v.distance
                from chunk_vectors v
                where v.embedding match $embedding
                  and v.rowid in (
                    select c.id
                    from chunks c
                    join temp.{ScopedSearchFilter.TempTableName} s on s.path = c.path
                  )
                  and k = $top
                order by v.distance
                """;
        command.AddParameter("$embedding", SqliteVectorSerializer.ToJson(queryEmbedding.Values));
        command.AddParameter("$top", topK);
        try
        {
            var result = new List<SemanticSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new SemanticSearchResult(reader.GetInt64(0), reader.GetDouble(1)));
            }

            return result;
        }
        catch (Exception ex) when (ex is not DimonSmart.LocalVectorSearchMcp.Core.Exceptions.ConfigurationException)
        {
            throw new VectorIndexException("Vector search failed.", ex);
        }
    }
}
