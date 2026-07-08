namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IKnowledgeSearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}
