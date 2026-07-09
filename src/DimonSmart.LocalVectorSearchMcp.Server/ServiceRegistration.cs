using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
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
        services.AddSingleton<IMarkdownChunker>(sp => sp.GetRequiredService<MarkdownChunker>());
        services.AddSingleton<IMarkdownDocumentLoader, MarkdownDocumentLoader>();
        services.AddSingleton<IMarkdownElementParser, MarkdownElementParser>();
        services.AddSingleton<KnowledgeBasePathGuard>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqliteSchemaInitializer>();
        services.AddSingleton<IIndexInitializer>(sp => sp.GetRequiredService<SqliteSchemaInitializer>());
        services.AddSingleton<SqliteIndexManifestService>();
        services.AddSingleton<IIndexManifestService>(sp => sp.GetRequiredService<SqliteIndexManifestService>());
        services.AddSingleton<SqliteDocumentDeletionService>();
        services.AddSingleton<SqliteDocumentIndexStore>();
        services.AddSingleton<IDocumentIndexStore>(sp => sp.GetRequiredService<SqliteDocumentIndexStore>());
        services.AddSingleton<SqliteSearchIndexReader>();
        services.AddSingleton<IChunkSearchDocumentReader>(sp => sp.GetRequiredService<SqliteSearchIndexReader>());
        services.AddSingleton<ISearchIndexStateReader>(sp => sp.GetRequiredService<SqliteSearchIndexReader>());
        services.AddSingleton<SqliteMarkdownSliceReader>();
        services.AddSingleton<IIndexedMarkdownSliceReader>(sp => sp.GetRequiredService<SqliteMarkdownSliceReader>());
        services.AddSingleton<SqliteIndexStatusReader>();
        services.AddSingleton<IIndexStatusReader>(sp => sp.GetRequiredService<SqliteIndexStatusReader>());
        services.AddSingleton<SqliteVectorIndexService>();
        services.AddSingleton<IVectorIndexService>(sp => sp.GetRequiredService<SqliteVectorIndexService>());
        services.AddSingleton<SqliteFullTextSearchService>();
        services.AddSingleton<IFullTextSearchService>(sp => sp.GetRequiredService<SqliteFullTextSearchService>());
        services.AddSingleton<IKnowledgeBaseIndexer, KnowledgeBaseIndexer>();
        services.AddSingleton<IKnowledgeSearchService, KnowledgeSearchService>();
        services.AddSingleton<ISemanticPointerReader, SemanticPointerReader>();
        services.AddHttpClient<IEmbeddingProvider, OpenAiCompatibleEmbeddingProvider>(
            client => client.Timeout = TimeSpan.FromSeconds(config.Embedding.TimeoutSeconds));
        return services;
    }
}
