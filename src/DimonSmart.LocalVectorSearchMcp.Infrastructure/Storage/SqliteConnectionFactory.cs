using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using Microsoft.Data.Sqlite;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteConnectionFactory(LocalVectorSearchMcpConfig config)
{
    public SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(config.Storage.Path)!);
        var connection = new SqliteConnection($"Data Source={config.Storage.Path}");
        connection.Open();
        var paths = KnowledgeBasePathGuard.GetPathComparer(config.KnowledgeBase.Root);
        connection.CreateCollation("kb_path", (a, b) => paths.Compare(a, b));

        using var command = connection.CreateCommand();
        command.CommandText = "pragma foreign_keys = on;";
        command.ExecuteNonQuery();

        return connection;
    }
}
