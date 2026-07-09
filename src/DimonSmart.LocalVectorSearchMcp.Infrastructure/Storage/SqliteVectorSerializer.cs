using System.Globalization;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

internal static class SqliteVectorSerializer
{
    public static string ToJson(float[] values)
        => "[" + string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture))) + "]";
}
