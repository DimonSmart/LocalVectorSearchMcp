using System.Text.RegularExpressions;
using DimonSmart.LocalVectorSearchMcp.Core.Search;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteFullTextSearchService(SqliteConnectionFactory factory) : IFullTextSearchService
{
    public async Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        var ftsQuery = SqliteFtsQueryBuilder.Build(query);
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
        command.AddParameter("$query", ftsQuery);
        command.AddParameter("$top", topK);
        try
        {
            var result = new List<LexicalSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new LexicalSearchResult(reader.GetInt64(0), reader.GetDouble(1), reader.GetString(2)));
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new FullTextSearchException("Full text search failed.", ex);
        }
    }
}

internal static class SqliteFtsQueryBuilder
{
    public static string Build(string query)
    {
        var terms = Regex.Matches(query, @"[\p{L}\p{N}_-]+")
            .Select(match => "\"" + match.Value.Replace("\"", "\"\"") + "\"");
        return string.Join(" AND ", terms);
    }
}
