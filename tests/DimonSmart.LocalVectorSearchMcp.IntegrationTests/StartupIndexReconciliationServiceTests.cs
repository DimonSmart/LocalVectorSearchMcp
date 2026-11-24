using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class StartupIndexReconciliationServiceTests
{
    [Fact]
    public async Task StartAsync_WhenWatchingIsEnabled_SchedulesChangedReindex()
    {
        var coordinator = new RecordingReindexCoordinator();
        var service = new StartupIndexReconciliationService(
            Config(watchFiles: true),
            coordinator,
            NullLogger<StartupIndexReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var request = Assert.IsType<ReindexRequest>(coordinator.LastRequest);
        Assert.Equal(ReindexScope.Changed, request.Scope);
        Assert.False(request.Force);
    }

    [Fact]
    public async Task StartAsync_WhenWatchingIsDisabled_DoesNotScheduleReindex()
    {
        var coordinator = new RecordingReindexCoordinator();
        var service = new StartupIndexReconciliationService(
            Config(watchFiles: false),
            coordinator,
            NullLogger<StartupIndexReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Null(coordinator.LastRequest);
    }

    private static LocalVectorSearchMcpConfig Config(bool watchFiles)
        => new()
        {
            KnowledgeBase = new KnowledgeBaseConfig
            {
                WatchFiles = watchFiles
            }
        };

    private sealed class RecordingReindexCoordinator : IReindexCoordinator
    {
        public ReindexRequest? LastRequest { get; private set; }

        public ReindexStartResponse TryStart(ReindexRequest request)
        {
            LastRequest = request;
            return new ReindexStartResponse(
                true,
                new ReindexCurrentOperation(
                    request.Scope,
                    request.Force,
                    DateTimeOffset.UtcNow));
        }

        public ReindexStatus GetStatus()
            => new(false, null, null);
    }
}
