using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DimonSmart.LocalVectorSearchMcp.Server;

internal static class MaintenanceCommandRunner
{
    public static async Task RunAsync(
        IServiceProvider services,
        MaintenanceCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Reindex)
        {
            var indexer = services.GetRequiredService<IKnowledgeBaseIndexer>();
            var response = await indexer.ReindexAsync(
                new ReindexRequest(ReindexScope.Changed, options.Force),
                cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(response, JsonOptions.Default));
            return;
        }

        if (options.Status)
        {
            var indexInitializer = services.GetRequiredService<IIndexInitializer>();
            var statusReader = services.GetRequiredService<IIndexStatusReader>();
            await indexInitializer.InitializeAsync(cancellationToken);
            var status = await statusReader.GetStatusAsync(cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(status, JsonOptions.Default));
            return;
        }

        throw new InvalidOperationException("No maintenance command was selected.");
    }
}
