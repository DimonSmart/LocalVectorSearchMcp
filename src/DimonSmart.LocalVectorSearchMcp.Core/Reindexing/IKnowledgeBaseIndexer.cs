namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

public interface IKnowledgeBaseIndexer
{
    Task<ReindexResponse> ReindexAsync(ReindexRequest request, CancellationToken cancellationToken);
}
