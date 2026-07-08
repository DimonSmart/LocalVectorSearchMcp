using System.Security.Cryptography;
using System.Text;

namespace DimonSmart.LocalVectorSearchMcp.Core.Storage;

public static class StableHash
{
    public static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
