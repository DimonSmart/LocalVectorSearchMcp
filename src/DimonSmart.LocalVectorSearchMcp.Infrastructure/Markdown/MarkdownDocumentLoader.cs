using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

public sealed class MarkdownDocumentLoader : IMarkdownDocumentLoader
{
    public async Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(KnowledgeBaseConfig knowledgeBase, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(knowledgeBase.Root);
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(knowledgeBase.Include);
        matcher.AddExcludePatterns(knowledgeBase.Exclude);
        var files = matcher.GetResultsInFullPath(root)
            .Where(file => Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var documents = new List<MarkdownSourceDocument>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await File.ReadAllTextAsync(file, cancellationToken);
            var markdown = MarkdownTextNormalizer.Normalize(raw);
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            documents.Add(new MarkdownSourceDocument(relative, Path.GetFullPath(file), markdown, StableHash.HashText(markdown), File.GetLastWriteTimeUtc(file)));
        }

        return documents;
    }
}
