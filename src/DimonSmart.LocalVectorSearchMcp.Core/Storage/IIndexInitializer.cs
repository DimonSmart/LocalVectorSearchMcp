namespace DimonSmart.LocalVectorSearchMcp.Core.Storage;

public interface IIndexInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
