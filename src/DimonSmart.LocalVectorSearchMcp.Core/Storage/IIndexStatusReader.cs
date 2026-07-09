using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

namespace DimonSmart.LocalVectorSearchMcp.Core.Storage;

public interface IIndexStatusReader
{
    Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}
