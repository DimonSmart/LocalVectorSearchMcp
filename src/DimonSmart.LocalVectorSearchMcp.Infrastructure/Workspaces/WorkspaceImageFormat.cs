using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

internal enum WorkspaceImageFormat
{
    Png,
    Jpeg,
    WebP,
    Gif
}

internal sealed record WorkspaceImageFormatInfo(
    WorkspaceImageFormat Format,
    string MimeType,
    string CanonicalExtension);

internal static class WorkspaceImageFormats
{
    public static WorkspaceImageFormatInfo? Detect(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length >= 8
            && prefix[0] == 0x89
            && prefix[1] == 0x50
            && prefix[2] == 0x4e
            && prefix[3] == 0x47
            && prefix[4] == 0x0d
            && prefix[5] == 0x0a
            && prefix[6] == 0x1a
            && prefix[7] == 0x0a)
        {
            return Info(WorkspaceImageFormat.Png);
        }

        if (prefix.Length >= 3
            && prefix[0] == 0xff
            && prefix[1] == 0xd8
            && prefix[2] == 0xff)
        {
            return Info(WorkspaceImageFormat.Jpeg);
        }

        if (prefix.Length >= 12
            && prefix[..4].SequenceEqual("RIFF"u8)
            && prefix.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return Info(WorkspaceImageFormat.WebP);
        }

        if (prefix.Length >= 6
            && (prefix[..6].SequenceEqual("GIF87a"u8)
                || prefix[..6].SequenceEqual("GIF89a"u8)))
        {
            return Info(WorkspaceImageFormat.Gif);
        }

        return null;
    }

    public static bool TryFromExtension(
        string extension,
        out WorkspaceImageFormatInfo info)
    {
        var format = extension.ToLowerInvariant() switch
        {
            ".png" => WorkspaceImageFormat.Png,
            ".jpg" or ".jpeg" => WorkspaceImageFormat.Jpeg,
            ".webp" => WorkspaceImageFormat.WebP,
            ".gif" => WorkspaceImageFormat.Gif,
            _ => (WorkspaceImageFormat?)null
        };

        if (format is null)
        {
            info = null!;
            return false;
        }

        info = Info(format.Value);
        return true;
    }

    public static bool ExtensionMatches(
        string extension,
        WorkspaceImageFormatInfo actual)
        => TryFromExtension(extension, out var declared)
           && declared.Format == actual.Format;

    public static void ValidateDeclaredMime(
        string? declaredMimeType,
        WorkspaceImageFormatInfo actual)
    {
        if (string.IsNullOrWhiteSpace(declaredMimeType)
            || declaredMimeType.Equals(
                "application/octet-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalized = declaredMimeType.Trim();
        var declared = normalized.ToLowerInvariant() switch
        {
            "image/png" => WorkspaceImageFormat.Png,
            "image/jpeg" or "image/jpg" => WorkspaceImageFormat.Jpeg,
            "image/webp" => WorkspaceImageFormat.WebP,
            "image/gif" => WorkspaceImageFormat.Gif,
            _ => (WorkspaceImageFormat?)null
        };

        if (declared is not null && declared.Value != actual.Format)
        {
            throw new WorkspaceImageException(
                $"Declared MIME type '{normalized}' does not match the detected {actual.MimeType} image.");
        }
    }

    private static WorkspaceImageFormatInfo Info(WorkspaceImageFormat format)
        => format switch
        {
            WorkspaceImageFormat.Png => new(format, "image/png", ".png"),
            WorkspaceImageFormat.Jpeg => new(format, "image/jpeg", ".jpg"),
            WorkspaceImageFormat.WebP => new(format, "image/webp", ".webp"),
            WorkspaceImageFormat.Gif => new(format, "image/gif", ".gif"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
}
