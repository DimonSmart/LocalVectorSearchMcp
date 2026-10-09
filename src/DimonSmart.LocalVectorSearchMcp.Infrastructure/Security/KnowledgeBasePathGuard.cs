using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

public sealed class KnowledgeBasePathGuard(LocalVectorSearchMcpConfig config)
{
    private static readonly char[] InvalidPortableCharacters =
        ['<', '>', ':', '"', '|', '?', '*'];

    public string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith('/')
            || path.StartsWith('\\')
            || Path.IsPathRooted(path))
        {
            throw new KnowledgeBaseAccessException(
                "A non-empty root-relative path is required.");
        }

        var normalized = path.Replace('\\', '/');
        foreach (var part in normalized.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(part)
                || part is "." or ".."
                || part.EndsWith(' ')
                || part.EndsWith('.')
                || part.Any(char.IsControl)
                || part.IndexOfAny(InvalidPortableCharacters) >= 0
                || IsReservedDeviceName(part))
            {
                throw new KnowledgeBaseAccessException(
                    "Path contains an invalid or unsafe component.");
            }
        }

        var absolute = Path.GetFullPath(Path.Combine(
            config.KnowledgeBase.Root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(config.KnowledgeBase.Root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (absolute.StartsWith(
                root + Path.DirectorySeparatorChar,
                PathComparison()))
        {
            return normalized;
        }

        throw new KnowledgeBaseAccessException(
            "Path is outside configured knowledge base root.");
    }

    public string ResolveWorkspacePath(string path)
    {
        var normalized = ValidateRelativePath(path);
        var root = Path.GetFullPath(config.KnowledgeBase.Root);
        var absolute = Path.GetFullPath(Path.Combine(
            root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureNoReparsePoints(root, absolute);
        return absolute;
    }

    public string ResolveMarkdownPath(string path)
    {
        var normalized = ValidateRelativePath(path);
        if (!Path.GetExtension(normalized).Equals(
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeBaseAccessException(
                "Markdown write operations require a .md path.");
        }

        return ResolveWorkspacePath(normalized);
    }

    public string ValidateImagePath(string path)
    {
        var normalized = ValidateRelativePath(path);
        if (normalized.Split('/').Any(component =>
                component.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        {
            throw new KnowledgeBaseAccessException(
                "Image operations cannot access Git metadata.");
        }

        var absolute = Path.GetFullPath(Path.Combine(
            config.KnowledgeBase.Root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        var storage = Path.GetFullPath(config.Storage.Path);
        var comparison = PathComparison();
        if (absolute.Equals(storage, comparison)
            || absolute.StartsWith(storage + "-", comparison)
            || absolute.StartsWith(storage + ".", comparison))
        {
            throw new KnowledgeBaseAccessException(
                "Image operations cannot access internal index files.");
        }

        return normalized;
    }

    public string ResolveImagePath(string path)
    {
        var normalized = ValidateImagePath(path);
        return ResolveWorkspacePath(normalized);
    }

    public string ValidateWorkspaceImagePath(string path)
        => ValidateImagePath(path);

    public string ResolveWorkspaceImagePath(string path)
        => ResolveImagePath(path);

    public IReadOnlyList<string> CreateImageParentDirectories(string path)
    {
        var normalized = ValidateImagePath(path);
        var absolute = ResolveImagePath(normalized);
        var root = Path.GetFullPath(config.KnowledgeBase.Root);
        var parent = Path.GetDirectoryName(absolute)!;
        var missing = new Stack<string>();
        var current = parent;
        while (!Directory.Exists(current))
        {
            if (File.Exists(current))
            {
                throw new KnowledgeBaseAccessException(
                    "A file occupies a destination directory.");
            }

            missing.Push(current);
            current = Path.GetDirectoryName(current)
                ?? throw new KnowledgeBaseAccessException(
                    "The destination is outside the workspace.");
        }

        var created = new List<string>();
        try
        {
            while (missing.Count > 0)
            {
                var directory = missing.Pop();
                Directory.CreateDirectory(directory);
                created.Add(directory);
                _ = ResolveImagePath(normalized);
            }

            return created;
        }
        catch
        {
            foreach (var directory in created.AsEnumerable().Reverse())
            {
                TryRemoveEmptyDirectory(directory);
            }

            throw;
        }
    }

    public static void TryRemoveEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureNoReparsePoints(string root, string absolute)
    {
        var relative = Path.GetRelativePath(root, absolute);
        var current = root;
        foreach (var part in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new KnowledgeBaseAccessException(
                        "Paths through symbolic links, junctions, or reparse points are not allowed.");
                }
            }
            catch (FileNotFoundException)
            {
                if (new FileInfo(current).LinkTarget is not null)
                {
                    throw new KnowledgeBaseAccessException(
                        "Paths through symbolic links are not allowed.");
                }
            }
            catch (DirectoryNotFoundException)
            {
                if (new FileInfo(current).LinkTarget is not null)
                {
                    throw new KnowledgeBaseAccessException(
                        "Paths through symbolic links are not allowed.");
                }
            }
        }
    }

    private static bool IsReservedDeviceName(string part)
    {
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.Ordinal)
                    || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] is >= '1' and <= '9');
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
