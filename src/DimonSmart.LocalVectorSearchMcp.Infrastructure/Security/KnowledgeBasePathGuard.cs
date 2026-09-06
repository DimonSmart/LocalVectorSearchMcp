using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

public sealed class KnowledgeBasePathGuard(LocalVectorSearchMcpConfig config)
{
    public string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new KnowledgeBaseAccessException("Path is required.");
        if (Path.IsPathRooted(path)) throw new KnowledgeBaseAccessException("Absolute paths are not allowed.");
        if (path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..")) throw new KnowledgeBaseAccessException("Path traversal is not allowed.");

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var absolute = Path.GetFullPath(Path.Combine(config.KnowledgeBase.Root, normalized));
        var root = Path.GetFullPath(config.KnowledgeBase.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (absolute.StartsWith(root, comparison))
        {
            return normalized;
        }

        throw new KnowledgeBaseAccessException("Path is outside configured knowledge base root.");
    }

    public string ResolveMarkdownPath(string path)
    {
        var normalized = ValidateRelativePath(path);
        if (!Path.GetExtension(normalized).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeBaseAccessException("Markdown write operations require a .md path.");
        }

        var root = Path.GetFullPath(config.KnowledgeBase.Root);
        var absolute = Path.GetFullPath(Path.Combine(
            root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureNoReparsePoints(root, absolute);
        return absolute;
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
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new KnowledgeBaseAccessException(
                    "Write operations through symbolic links, junctions, or reparse points are not allowed.");
            }
        }
    }
}
