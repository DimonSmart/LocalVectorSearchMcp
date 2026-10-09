using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

public sealed class KnowledgeBasePathGuard(LocalVectorSearchMcpConfig config)
{
    public string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new KnowledgeBaseAccessException("Path is required.");
        }

        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\')
            || (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':'))
        {
            throw new KnowledgeBaseAccessException("Absolute paths are not allowed.");
        }

        if (path.Any(char.IsControl))
        {
            throw new KnowledgeBaseAccessException("Control characters are not allowed in paths.");
        }

        if (path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part == ".."))
        {
            throw new KnowledgeBaseAccessException("Path traversal is not allowed.");
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var absolute = Path.GetFullPath(Path.Combine(
            config.KnowledgeBase.Root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(config.KnowledgeBase.Root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = PathComparison();

        if (absolute.Equals(root, comparison)
            || absolute.StartsWith(root + Path.DirectorySeparatorChar, comparison))
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

    // images/ is the default save directory, not an access boundary.
    public string ValidateImagePath(string path)
    {
        var normalized = ValidateRelativePath(path);
        if (normalized is "." or "" || normalized.EndsWith('/')
            || normalized.Split('/').Any(part => part is "." or "" or "..")
            || path.Contains('\\'))
        {
            throw new KnowledgeBaseAccessException("A normalized workspace-relative image file path is required.");
        }

        return normalized;
    }

    public string ResolveImagePath(string path)
        => ResolveWorkspacePath(ValidateImagePath(path));

    public string ValidateWorkspaceImagePath(string path)
        => ValidateImagePath(path);

    public string ResolveWorkspaceImagePath(string path)
        => ResolveImagePath(path);

    public string ValidateImageDirectory(string? directory)
    {
        var value = directory ?? "images";
        if (value == ".") return value;
        var normalized = ValidateImagePath(value);
        return normalized;
    }

    public string ResolveImageDirectory(string? directory)
    {
        var normalized = ValidateImageDirectory(directory);
        return normalized == "."
            ? Path.GetFullPath(config.KnowledgeBase.Root)
            : ResolveWorkspacePath(normalized);
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
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new KnowledgeBaseAccessException(
                    "Paths through symbolic links, junctions, or reparse points are not allowed.");
            }
        }
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
