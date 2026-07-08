namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IFullTextSearchService
{
    Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(string query, int topK, string? knowledgeBase, CancellationToken cancellationToken);
}
