using System.Text.RegularExpressions;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteFullTextSearchService(SqliteConnectionFactory factory) : IFullTextSearchService
{
    public Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
        => SearchAsync(query, topK, null, cancellationToken);

    public async Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(
        string query,
        int topK,
        SearchPathScope? scope,
        CancellationToken cancellationToken)
    {
        var ftsQuery = SqliteFtsQueryBuilder.Build(query);
        if (ftsQuery.Length == 0) return [];

        await using var db = factory.Open();
        var command = db.CreateCommand();
        command.CommandText = """
            select f.rowid, bm25(chunks_fts) as score,
                   snippet(chunks_fts, 0, '[', ']', '...', 16) as snippet,
                   c.path
            from chunks_fts f
            join chunks c on c.id = f.rowid
            where chunks_fts match $query
            order by score
            limit $top
            """;
        command.AddParameter("$query", ftsQuery);
        command.AddParameter("$top", scope is null ? topK : int.MaxValue);
        try
        {
            var matcher = scope is null ? null : new PathScopeMatcher(scope);
            var result = new List<LexicalSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (matcher is not null && !matcher.Matches(reader.GetString(3))) continue;
                result.Add(new LexicalSearchResult(reader.GetInt64(0), reader.GetDouble(1), reader.GetString(2)));
                if (result.Count == topK) break;
            }

            return result;
        }
        catch (Exception ex) when (ex is not DimonSmart.LocalVectorSearchMcp.Core.Exceptions.ConfigurationException)
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
