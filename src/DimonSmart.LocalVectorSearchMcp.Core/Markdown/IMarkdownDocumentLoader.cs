using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public interface IMarkdownDocumentLoader
{
    Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(KnowledgeBaseConfig knowledgeBase, CancellationToken cancellationToken);
    Task<MarkdownSourceDocument> LoadFileAsync(
        KnowledgeBaseConfig knowledgeBase,
        string relativePath,
        CancellationToken cancellationToken);
}
