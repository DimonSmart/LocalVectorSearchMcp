using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

internal static class ImageFileNamePolicy
{
    private static readonly char[] PortableInvalidCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Resolve(
        string? explicitFileName,
        string? sourceFileName,
        WorkspaceImageFormatInfo format)
    {
        if (explicitFileName is not null)
        {
            return ResolveExplicit(explicitFileName, format);
        }

        var sourceName = TryResolveSourceName(sourceFileName, format);
        if (sourceName is not null)
        {
            return sourceName;
        }

        var random = Guid.NewGuid().ToString("N")[..12];
        return $"image-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{random}{format.CanonicalExtension}";
    }

    public static string WithCollisionSuffix(string fileName, int suffix)
    {
        var extension = Path.GetExtension(fileName);
        var stem = fileName[..^extension.Length];
        return $"{stem}-{suffix}{extension}";
    }

    public static string BuildMarkdown(string path, string? altText)
    {
        var alt = (altText ?? "")
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

        var destination = path.Any(char.IsWhiteSpace)
                          || path.Contains('(')
                          || path.Contains(')')
            ? $"<{path}>"
            : path;

        return $"![{alt}]({destination})";
    }

    private static string ResolveExplicit(
        string fileName,
        WorkspaceImageFormatInfo format)
    {
        ValidatePortableBaseName(fileName, "fileName");

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return fileName + format.CanonicalExtension;
        }

        if (!WorkspaceImageFormats.ExtensionMatches(extension, format))
        {
            throw new WorkspaceImageException(
                $"fileName extension '{extension}' does not match the detected {format.MimeType} image.");
        }

        return fileName;
    }

    private static string? TryResolveSourceName(
        string? sourceFileName,
        WorkspaceImageFormatInfo format)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            return null;
        }

        var normalized = sourceFileName.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        var baseName = lastSlash >= 0
            ? normalized[(lastSlash + 1)..]
            : normalized;
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        var extension = Path.GetExtension(baseName);
        var stem = string.IsNullOrEmpty(extension)
            ? baseName
            : baseName[..^extension.Length];

        try
        {
            ValidatePortableBaseName(stem, "file.file_name");
        }
        catch (WorkspaceImageException)
        {
            return null;
        }

        return stem + format.CanonicalExtension;
    }

    private static void ValidatePortableBaseName(string value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new WorkspaceImageException(
                $"{source} must be a non-empty file basename.");
        }

        if (value is "." or "..")
        {
            throw new WorkspaceImageException(
                $"{source} must be a file basename.");
        }

        if (value.EndsWith(' ') || value.EndsWith('.'))
        {
            throw new WorkspaceImageException(
                $"{source} cannot end with a space or dot.");
        }

        if (value.Any(char.IsControl)
            || value.IndexOfAny(PortableInvalidCharacters) >= 0
            || Path.IsPathRooted(value))
        {
            throw new WorkspaceImageException(
                $"{source} contains characters or path components that are not allowed in a portable file basename.");
        }

        var firstDot = value.IndexOf('.');
        var deviceStem = (firstDot >= 0 ? value[..firstDot] : value)
            .ToUpperInvariant();
        if (deviceStem is "CON" or "PRN" or "AUX" or "NUL"
            || IsNumberedDevice(deviceStem, "COM")
            || IsNumberedDevice(deviceStem, "LPT"))
        {
            throw new WorkspaceImageException(
                $"{source} uses a Windows reserved device name.");
        }
    }

    private static bool IsNumberedDevice(string value, string prefix)
        => value.Length == 4
           && value.StartsWith(prefix, StringComparison.Ordinal)
           && value[3] is >= '1' and <= '9';
}
