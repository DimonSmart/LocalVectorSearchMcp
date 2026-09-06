using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

public sealed class MarkdownDocumentLoader : IMarkdownDocumentLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(
        KnowledgeBaseConfig knowledgeBase,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(knowledgeBase.Root);
        var matcher = CreateMatcher(knowledgeBase);
        var files = matcher.GetResultsInFullPath(root)
            .Select(file => new
            {
                FullPath = file,
                RelativePath = Path.GetRelativePath(root, file).Replace('\\', '/')
            })
            .Where(file => Path.GetExtension(file.FullPath).Equals(".md", StringComparison.OrdinalIgnoreCase)
                && File.Exists(file.FullPath))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        var documents = new List<MarkdownSourceDocument>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.Add(await LoadFileCoreAsync(file.RelativePath, file.FullPath, cancellationToken));
        }

        return documents;
    }

    public Task<MarkdownSourceDocument> LoadFileAsync(
        KnowledgeBaseConfig knowledgeBase,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(
            knowledgeBase.Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return LoadFileCoreAsync(relativePath.Replace('\\', '/'), absolutePath, cancellationToken);
    }

    private static async Task<MarkdownSourceDocument> LoadFileCoreAsync(
        string relativePath,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var content = hasBom ? bytes.AsSpan(Encoding.UTF8.Preamble.Length) : bytes.AsSpan();
        string markdown;
        try
        {
            markdown = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ConfigurationException($"Markdown file '{relativePath}' is not valid UTF-8.", exception);
        }

        return new MarkdownSourceDocument(
            relativePath,
            Path.GetFullPath(absolutePath),
            markdown,
            StableHash.HashText(MarkdownTextNormalizer.Normalize(markdown)),
            File.GetLastWriteTimeUtc(absolutePath),
            hasBom,
            StableHash.HashBytes(bytes));
    }

    private static Matcher CreateMatcher(KnowledgeBaseConfig knowledgeBase)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in knowledgeBase.Include)
        {
            AddPattern(value => { matcher.AddInclude(value); }, "include", pattern);
        }

        foreach (var pattern in knowledgeBase.Exclude)
        {
            AddPattern(value => { matcher.AddExclude(value); }, "exclude", pattern);
        }

        return matcher;
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
