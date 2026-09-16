using DimonSmart.LocalVectorSearchMcp.Core.Search;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;

internal static class ScopedSearchFilter
{
    public const string TempTableName = "search_scope_paths";

    public static async Task<bool> PrepareAsync(
        SqliteConnection db,
        SearchPathScope scope,
        CancellationToken cancellationToken)
    {
        var matcher = new PathScopeMatcher(scope);
        var eligiblePaths = new List<string>();
        var select = db.CreateCommand();
        select.CommandText = "select distinct path from chunks";
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var path = reader.GetString(0);
                if (matcher.Matches(path))
                {
                    eligiblePaths.Add(path);
                }
            }
        }

        if (eligiblePaths.Count == 0)
        {
            return false;
        }

        var create = db.CreateCommand();
        create.CommandText = $"""
            drop table if exists temp.{TempTableName};
            create temp table {TempTableName} (
              path text primary key
            ) without rowid;
            """;
        await create.ExecuteNonQueryAsync(cancellationToken);

        using var transaction = db.BeginTransaction();
        var insert = db.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"insert into temp.{TempTableName}(path) values($path)";
        var pathParameter = insert.Parameters.Add("$path", SqliteType.Text);
        foreach (var path in eligiblePaths)
        {
            pathParameter.Value = path;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return true;
    }
}
