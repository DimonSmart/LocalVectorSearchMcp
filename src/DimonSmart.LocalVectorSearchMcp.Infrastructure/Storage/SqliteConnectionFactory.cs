using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteConnectionFactory(LocalVectorSearchMcpConfig config)
{
    public SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(config.Storage.Path)!);
        var connection = new SqliteConnection($"Data Source={config.Storage.Path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "pragma foreign_keys = on;";
        command.ExecuteNonQuery();

        return connection;
    }
}
