namespace DimonSmart.LocalVectorSearchMcp.Core;

public interface IKnowledgeBaseIndexer
{
    Task<ReindexResponse> ReindexAsync(ReindexRequest request, CancellationToken cancellationToken);
}
