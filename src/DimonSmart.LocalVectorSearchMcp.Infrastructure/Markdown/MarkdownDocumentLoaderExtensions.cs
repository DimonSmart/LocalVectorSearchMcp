using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

internal static class MarkdownDocumentLoaderExtensions
{
    public static async Task<MarkdownSourceDocument> LoadExistingAsync(
        this IMarkdownDocumentLoader loader,
        KnowledgeBaseConfig knowledgeBase,
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await loader.LoadFileAsync(
                knowledgeBase,
                relativePath,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DocumentNotFoundException(relativePath, exception);
        }
    }
}
