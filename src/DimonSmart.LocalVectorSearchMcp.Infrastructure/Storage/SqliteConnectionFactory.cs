using DimonSmart.LocalVectorSearchMcp.Core;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure;

public sealed class SqliteConnectionFactory(LocalVectorSearchMcpConfig config)
{
    public SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(config.Storage.Path)!);
        var connection = new SqliteConnection($"Data Source={config.Storage.Path}");
        connection.Open();
        return connection;
    }
}
