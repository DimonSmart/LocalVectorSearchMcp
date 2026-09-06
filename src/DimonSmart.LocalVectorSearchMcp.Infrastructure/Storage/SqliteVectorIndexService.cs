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
        var candidateCount = topK;
        if (scope is not null)
        {
            candidateCount = (int)(await db.ScalarLongAsync(
                "select count(*) from chunk_vectors",
                [],
                cancellationToken) ?? 0);
            if (candidateCount == 0) return [];
        }

        var command = db.CreateCommand();
        command.CommandText = """
            select v.rowid, v.distance, c.path
            from chunk_vectors v
            join chunks c on c.id = v.rowid
            where v.embedding match $embedding and k = $top
            order by v.distance
            """;
        command.AddParameter("$embedding", SqliteVectorSerializer.ToJson(queryEmbedding.Values));
        command.AddParameter("$top", candidateCount);
        try
        {
            var matcher = scope is null ? null : new PathScopeMatcher(scope);
            var result = new List<SemanticSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (matcher is not null && !matcher.Matches(reader.GetString(2))) continue;
                result.Add(new SemanticSearchResult(reader.GetInt64(0), reader.GetDouble(1)));
                if (result.Count == topK) break;
            }

            return result;
        }
        catch (Exception ex) when (ex is not DimonSmart.LocalVectorSearchMcp.Core.Exceptions.ConfigurationException)
        {
            throw new VectorIndexException("Vector search failed.", ex);
        }
    }
}
