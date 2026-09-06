namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public interface IFullTextSearchService
{
    Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(
        string query,
        int topK,
        SearchPathScope? scope,
        CancellationToken cancellationToken);
}
