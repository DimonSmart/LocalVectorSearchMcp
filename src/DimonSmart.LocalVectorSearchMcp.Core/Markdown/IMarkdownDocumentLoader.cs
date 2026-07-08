namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IMarkdownDocumentLoader
{
    Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(KnowledgeBaseConfig knowledgeBase, CancellationToken cancellationToken);
}
