using DimonSmart.LocalVectorSearchMcp.Core.Search;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

internal static class SqliteVectorExtensionLoader
{
    public static void Load(SqliteConnection db)
    {
        try
        {
            db.EnableExtensions();
            db.LoadVector();
        }
        catch (Exception ex)
        {
            throw new VectorIndexException("sqlite-vec initialization failed. Ensure vec0 native extension is available.", ex);
        }
    }
}
