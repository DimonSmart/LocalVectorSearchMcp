using System.Buffers;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticFingerprint
{
    private const int StackBufferSize = 1024;

    public static string Compute(string exactSource)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        return Compute(exactSource.AsSpan());
    }

    public static string Compute(ReadOnlySpan<char> exactSource)
    {
        var byteCount = Encoding.UTF8.GetByteCount(exactSource);
        byte[]? rented = null;
        Span<byte> bytes = byteCount <= StackBufferSize
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            var written = Encoding.UTF8.GetBytes(exactSource, bytes);
            var value = XxHash64.HashToUInt64(bytes[..written]);
            return value.ToString("x16", CultureInfo.InvariantCulture);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
