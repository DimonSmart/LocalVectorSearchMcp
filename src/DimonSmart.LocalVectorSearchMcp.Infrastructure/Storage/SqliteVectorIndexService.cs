using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Search;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteVectorIndexService(SqliteConnectionFactory factory) : IVectorIndexService
{
    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        EmbeddingVector queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        SqliteVectorExtensionLoader.Load(db);
        var command = db.CreateCommand();
        command.CommandText = """
            select v.rowid, v.distance
            from chunk_vectors v
            where v.embedding match $embedding and k = $top
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
        catch (Exception ex)
        {
            throw new VectorIndexException("Vector search failed.", ex);
        }
    }
}
