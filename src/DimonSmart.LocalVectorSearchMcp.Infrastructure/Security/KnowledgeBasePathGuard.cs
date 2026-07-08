using DimonSmart.LocalVectorSearchMcp.Core;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure;

public sealed class KnowledgeBasePathGuard(LocalVectorSearchMcpConfig config)
{
    public string ValidateRelativePath(string? knowledgeBase, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new KnowledgeBaseAccessException("Path is required.");
        if (Path.IsPathRooted(path)) throw new KnowledgeBaseAccessException("Absolute paths are not allowed.");
        if (path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..")) throw new KnowledgeBaseAccessException("Path traversal is not allowed.");

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var candidates = config.KnowledgeBases.Where(kb => knowledgeBase is null || kb.Name == knowledgeBase).ToList();
        if (candidates.Count == 0) throw new KnowledgeBaseAccessException($"Knowledge base was not found: {knowledgeBase}");

        foreach (var kb in candidates)
        {
            var absolute = Path.GetFullPath(Path.Combine(kb.Root, normalized));
            var root = Path.GetFullPath(kb.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }
        }

        throw new KnowledgeBaseAccessException("Path is outside configured knowledge base root.");
    }
}
