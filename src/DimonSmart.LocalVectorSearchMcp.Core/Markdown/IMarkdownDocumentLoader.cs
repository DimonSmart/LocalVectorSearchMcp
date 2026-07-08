using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public interface IMarkdownDocumentLoader
{
    Task<IReadOnlyList<MarkdownSourceDocument>> LoadAsync(KnowledgeBaseConfig knowledgeBase, CancellationToken cancellationToken);
}
