using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Serialization.Metadata;

var isMaintenance = args.Contains("--reindex") || args.Contains("--status");

try
{
    var config = LocalVectorSearchConfigLoader.Load(args);
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddLocalVectorSearchMcp(config);

    if (isMaintenance)
    {
        using var host = builder.Build();
        if (args.Contains("--reindex"))
        {
            var indexer = host.Services.GetRequiredService<IKnowledgeBaseIndexer>();
            var response = await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, args.Contains("--force")), CancellationToken.None);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response, JsonOptions.Default));
            return;
        }

        var indexInitializer = host.Services.GetRequiredService<IIndexInitializer>();
        var statusReader = host.Services.GetRequiredService<IIndexStatusReader>();
        await indexInitializer.InitializeAsync(CancellationToken.None);
        var status = await statusReader.GetStatusAsync(CancellationToken.None);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(status, JsonOptions.Default));
        return;
    }

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(KnowledgeMcpTools).Assembly, JsonOptions.Default);

    await builder.Build().RunAsync();
}
catch (Exception ex) when (ex is ConfigurationException or KnowledgeBaseAccessException or EmbeddingProviderException or IndexCompatibilityException or IndexNotReadyException or VectorIndexException or FullTextSearchException or SemanticPointerNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}

namespace DimonSmart.LocalVectorSearchMcp.Server
{
    public static class JsonOptions
    {
        public static readonly System.Text.Json.JsonSerializerOptions Default = CreateDefault();

        private static System.Text.Json.JsonSerializerOptions CreateDefault()
        {
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
            options.Converters.Add(new SearchModeJsonConverter());
            options.Converters.Add(new ReindexScopeJsonConverter());
            return options;
        }
    }

    public static class ServiceRegistration
    {
        public static IServiceCollection AddLocalVectorSearchMcp(this IServiceCollection services, LocalVectorSearchMcpConfig config)
        {
            services.AddSingleton(config);
            services.AddSingleton<EmbeddingTextBuilder>();
            services.AddSingleton(sp => new MarkdownChunker(config.Chunking, sp.GetRequiredService<EmbeddingTextBuilder>()));
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
            services.AddHttpClient<IEmbeddingProvider, OpenAiCompatibleEmbeddingProvider>(client => client.Timeout = TimeSpan.FromSeconds(config.Embedding.TimeoutSeconds));
            return services;
        }
    }

    public sealed record ReindexToolRequest(ReindexScope Scope = ReindexScope.Changed, bool Force = false);
    public sealed record SearchToolRequest(string Query, SearchMode? Mode = null, int? TopK = null);
    public sealed record ReadToolRequest(string Path, string Pointer, int? MaxElements = null, int? MaxBytes = null);

    [McpServerToolType]
    public sealed class KnowledgeMcpTools(IKnowledgeBaseIndexer indexer, IIndexInitializer indexInitializer, IIndexStatusReader statusReader, IKnowledgeSearchService searchService, ISemanticPointerReader reader)
    {
        [McpServerTool(Name = "kb_reindex"), Description("Indexes or reindexes the current project's configured Markdown root.")]
        public Task<ReindexResponse> ReindexAsync(ReindexToolRequest request, CancellationToken cancellationToken)
        {
            return indexer.ReindexAsync(new ReindexRequest(request.Scope, request.Force), cancellationToken);
        }

        [McpServerTool(Name = "kb_status"), Description("Returns local vector search index status.")]
        public async Task<StatusResponse> StatusAsync(CancellationToken cancellationToken)
        {
            await indexInitializer.InitializeAsync(cancellationToken);
            return await statusReader.GetStatusAsync(cancellationToken);
        }

        [McpServerTool(Name = "kb_search"), Description("Searches the local Markdown knowledge base using lexical, semantic or hybrid search.")]
        public Task<SearchResponse> SearchAsync(SearchToolRequest request, CancellationToken cancellationToken)
        {
            int? topK = request.TopK is null ? null : Math.Clamp(request.TopK.Value, 1, 50);
            return searchService.SearchAsync(new SearchRequest(request.Query, request.Mode, topK), cancellationToken);
        }

        [McpServerTool(Name = "kb_read"), Description("Reads indexed Markdown content from a document starting at a semantic pointer.")]
        public Task<MarkdownSlice> ReadAsync(ReadToolRequest request, CancellationToken cancellationToken)
        {
            var pointer = SemanticPointerParser.Parse(request.Pointer);
            return reader.ReadAsync(request.Path, pointer, request.MaxElements ?? 20, request.MaxBytes ?? 12000, cancellationToken);
        }
    }
}
