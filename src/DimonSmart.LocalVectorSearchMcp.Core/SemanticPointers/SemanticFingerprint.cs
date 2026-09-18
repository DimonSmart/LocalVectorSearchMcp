using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticFingerprint
{
    public static string Compute(string exactSource)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        var value = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(exactSource));
        return value.ToString("x16", CultureInfo.InvariantCulture);
    }
}
