using DimonSmart.LocalVectorSearchMcp.Core;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure;

public sealed class MarkdownDocumentLoader : IMarkdownDocumentLoader
{
    public async Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(KnowledgeBaseConfig knowledgeBase, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(knowledgeBase.Root);
        var files = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(file => !IsExcluded(root, file, knowledgeBase.Exclude))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var documents = new List<MarkdownSourceDocument>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await File.ReadAllTextAsync(file, cancellationToken);
            var markdown = MarkdownTextNormalizer.Normalize(raw);
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            documents.Add(new MarkdownSourceDocument(knowledgeBase.Name, relative, Path.GetFullPath(file), markdown, StableHash.HashText(markdown), File.GetLastWriteTimeUtc(file)));
        }

        return documents;
    }

    private static bool IsExcluded(string root, string file, IReadOnlyList<string> excludes)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return excludes.Any(pattern =>
        {
            var p = pattern.Replace('\\', '/').Trim('*');
            return p.Length > 0 && relative.Contains(p.Trim('/'), StringComparison.OrdinalIgnoreCase);
        });
    }
}
