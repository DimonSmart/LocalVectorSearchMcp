using System.Buffers;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticFingerprint
{
    private const int StackBufferSize = 1024;

    public static string ComputeSegments(
        string source,
        IReadOnlyList<DimonSmart.LocalVectorSearchMcp.Core.Markdown.SourceRange> segments)
    {
        if (segments.Count == 0) return Compute("");
        if (segments.Count == 1)
        {
            var part = segments[0];
            return Compute(source.AsSpan(part.Start, part.Length));
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(segments.Count);
            foreach (var range in segments)
            {
                var bytes = Encoding.UTF8.GetBytes(source.AsSpan(range.Start, range.Length));
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }

        return XxHash64.HashToUInt64(stream.ToArray())
            .ToString("x16", CultureInfo.InvariantCulture);
    }

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
