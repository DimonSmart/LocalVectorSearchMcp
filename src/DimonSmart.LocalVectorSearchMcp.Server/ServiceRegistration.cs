using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Transfers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace DimonSmart.LocalVectorSearchMcp.Server;

public static class ServiceRegistration
{
    public static IServiceCollection AddLocalVectorSearchMcp(
        this IServiceCollection services,
        LocalVectorSearchMcpConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton<EmbeddingTextBuilder>();
        services.AddSingleton(sp => new MarkdownChunker(
            config.Chunking,
            sp.GetRequiredService<EmbeddingTextBuilder>()));
        services.AddSingleton<IMarkdownChunker>(
            sp => sp.GetRequiredService<MarkdownChunker>());
        services.AddSingleton<IMarkdownDocumentLoader, MarkdownDocumentLoader>();
        services.AddSingleton<IMarkdownElementParser, MarkdownElementParser>();
        services.AddSingleton<KnowledgeBasePathGuard>();
        services.AddSingleton<IndexOperationGate>();
        services.AddSingleton<InMemoryIndexSynchronizationState>();
        services.AddSingleton<IIndexSynchronizationState>(
            sp => sp.GetRequiredService<InMemoryIndexSynchronizationState>());
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqliteSchemaInitializer>();
        services.AddSingleton<IIndexInitializer>(
            sp => sp.GetRequiredService<SqliteSchemaInitializer>());
        services.AddSingleton<SqliteIndexManifestService>();
        services.AddSingleton<IIndexManifestService>(
            sp => sp.GetRequiredService<SqliteIndexManifestService>());
        services.AddSingleton<SqliteDocumentDeletionService>();
        services.AddSingleton<SqliteDocumentIndexStore>();
        services.AddSingleton<IDocumentIndexStore>(
            sp => sp.GetRequiredService<SqliteDocumentIndexStore>());
        services.AddSingleton<SqliteSearchIndexReader>();
        services.AddSingleton<IChunkSearchDocumentReader>(
            sp => sp.GetRequiredService<SqliteSearchIndexReader>());
        services.AddSingleton<ISearchIndexStateReader>(
            sp => sp.GetRequiredService<SqliteSearchIndexReader>());
        services.AddSingleton<SourceMarkdownSliceReader>();
        services.AddSingleton<IMarkdownSliceReader>(
            sp => sp.GetRequiredService<SourceMarkdownSliceReader>());
        services.AddSingleton<SqliteIndexStatusReader>();
        services.AddSingleton<IIndexStatusReader>(
            sp => sp.GetRequiredService<SqliteIndexStatusReader>());
        services.AddSingleton<SqliteVectorIndexService>();
        services.AddSingleton<IVectorIndexService>(
            sp => sp.GetRequiredService<SqliteVectorIndexService>());
        services.AddSingleton<SqliteFullTextSearchService>();
        services.AddSingleton<IFullTextSearchService>(
            sp => sp.GetRequiredService<SqliteFullTextSearchService>());
        services.AddSingleton<IKnowledgeBaseIndexer, KnowledgeBaseIndexer>();
        services.AddSingleton<ReindexCoordinator>();
        services.AddSingleton<IReindexCoordinator>(
            sp => sp.GetRequiredService<ReindexCoordinator>());
        services.AddSingleton<IReindexStateReader>(
            sp => sp.GetRequiredService<ReindexCoordinator>());
        services.AddSingleton<IWorkspaceIndexSynchronizer, WorkspaceIndexSynchronizer>();
        services.AddSingleton<WorkspaceIndexSynchronizationScheduler>();
        services.AddSingleton<IWorkspaceIndexSynchronizationScheduler>(
            sp => sp.GetRequiredService<WorkspaceIndexSynchronizationScheduler>());
        services.AddSingleton<IKnowledgeSearchService, KnowledgeSearchService>();
        services.AddSingleton<ISemanticPointerReader, SemanticPointerReader>();
        services.AddSingleton<IWorkspaceMutationService, WorkspaceMutationService>();
        services.AddSingleton<IWorkspaceNavigationService, WorkspaceNavigationService>();
        services.AddTransient<IWorkspaceImageService, WorkspaceImageService>();
        services.AddHostedService<MarkdownWorkspaceWatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<WorkspaceIndexSynchronizationScheduler>());
        services.AddHostedService(sp => sp.GetRequiredService<ReindexCoordinator>());

        services
            .AddHttpClient<IEmbeddingProvider, OpenAiCompatibleEmbeddingProvider>(
                client => client.Timeout =
                    TimeSpan.FromSeconds(config.Embedding.TimeoutSeconds))
            .RemoveAllLoggers();
        services
            .AddHttpClient<IRemoteFileDownloader, SecureFileDownloader>(
                client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(
                SecureFileDownloader.CreatePrimaryHandler);

        return services;
    }
}
