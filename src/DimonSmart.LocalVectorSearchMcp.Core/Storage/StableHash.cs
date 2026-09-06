using System.Security.Cryptography;
using System.Text;

namespace DimonSmart.LocalVectorSearchMcp.Core.Storage;

public static class StableHash
{
    public static string HashBytes(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string HashText(string text)
        => HashBytes(Encoding.UTF8.GetBytes(text));
}
