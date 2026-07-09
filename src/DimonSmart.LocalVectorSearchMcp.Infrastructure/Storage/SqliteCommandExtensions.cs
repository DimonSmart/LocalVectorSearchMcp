using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

internal static class SqliteCommandExtensions
{
    public static void AddParameter(this SqliteCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    public static async Task ExecuteAsync(
        this SqliteConnection db,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<(string Name, object? Value)>? parameters = null,
        SqliteTransaction? transaction = null)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (parameters is not null)
        {
            foreach (var parameter in parameters) command.AddParameter(parameter.Name, parameter.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<long?> ScalarLongAsync(
        this SqliteConnection db,
        string sql,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.AddParameter(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    public static async Task<string?> ScalarStringAsync(
        this SqliteConnection db,
        string sql,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.AddParameter(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value);
    }
}
