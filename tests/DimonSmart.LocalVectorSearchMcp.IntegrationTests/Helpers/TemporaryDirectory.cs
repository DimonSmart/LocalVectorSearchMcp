using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        // Windows may keep SQLite file handles briefly after clearing connection pools.
        // Retry only transient IO failures; persistent locks still fail the test.
        for (var attempt = 0; ; attempt++)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Path, true);
                return;
            }
            catch (IOException) when (OperatingSystem.IsWindows() && attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }
}
