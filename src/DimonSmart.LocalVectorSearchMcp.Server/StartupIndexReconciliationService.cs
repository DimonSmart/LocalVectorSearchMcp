using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DimonSmart.LocalVectorSearchMcp.Server;

public sealed class StartupIndexReconciliationService(
    LocalVectorSearchMcpConfig config,
    IReindexCoordinator reindexCoordinator,
    ILogger<StartupIndexReconciliationService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!config.KnowledgeBase.WatchFiles)
        {
            return Task.CompletedTask;
        }

        var response = reindexCoordinator.TryStart(
            new ReindexRequest(ReindexScope.Changed, false));
        if (response.Started)
        {
            logger.LogInformation(
                "Startup index reconciliation scheduled because file watching is enabled.");
        }
        else
        {
            logger.LogDebug(
                "Startup index reconciliation skipped because a reindex operation is already active.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
