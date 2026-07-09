using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

public sealed class MarkdownDocumentLoader : IMarkdownDocumentLoader
{
    public async Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(KnowledgeBaseConfig knowledgeBase, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(knowledgeBase.Root);
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in knowledgeBase.Include)
        {
            AddPattern(value => { matcher.AddInclude(value); }, "include", pattern);
        }

        foreach (var pattern in knowledgeBase.Exclude)
        {
            AddPattern(value => { matcher.AddExclude(value); }, "exclude", pattern);
        }

        var files = matcher.GetResultsInFullPath(root)
            .Select(file => new
            {
                FullPath = file,
                RelativePath = Path.GetRelativePath(root, file).Replace('\\', '/')
            })
            .Where(file => Path.GetExtension(file.FullPath).Equals(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(file.FullPath))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        var documents = new List<MarkdownSourceDocument>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await File.ReadAllTextAsync(file.FullPath, cancellationToken);
            var markdown = MarkdownTextNormalizer.Normalize(raw);
            documents.Add(new MarkdownSourceDocument(file.RelativePath, Path.GetFullPath(file.FullPath), markdown, StableHash.HashText(markdown), File.GetLastWriteTimeUtc(file.FullPath)));
        }

        return documents;
    }

    private static void AddPattern(Action<string> addPattern, string section, string pattern)
    {
        try
        {
            addPattern(pattern);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ConfigurationException(
                $"Invalid knowledgeBase.{section} pattern: '{pattern}'.",
                exception);
        }
    }
}
