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
}
