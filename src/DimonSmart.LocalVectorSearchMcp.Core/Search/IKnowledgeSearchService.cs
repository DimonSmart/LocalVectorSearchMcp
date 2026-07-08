namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public interface IKnowledgeSearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}
