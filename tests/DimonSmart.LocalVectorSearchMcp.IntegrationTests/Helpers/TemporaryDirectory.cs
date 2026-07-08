using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Path, true);
    }
}
