using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

internal sealed class SqliteTestServices
{
    public required SqliteSchemaInitializer Initializer { get; init; }
    public required SqliteIndexManifestService Manifest { get; init; }
    public required SqliteDocumentIndexStore DocumentStore { get; init; }
    public required SqliteSearchIndexReader SearchIndexReader { get; init; }
    public required SqliteMarkdownSliceReader SliceReader { get; init; }
    public required SqliteIndexStatusReader StatusReader { get; init; }
    public required SqliteVectorIndexService VectorSearch { get; init; }
    public required SqliteFullTextSearchService FullTextSearch { get; init; }

    public static SqliteTestServices Create(LocalVectorSearchMcpConfig config)
    {
        var factory = new SqliteConnectionFactory(config);
        var initializer = new SqliteSchemaInitializer(factory, config);
        return new SqliteTestServices
        {
            Initializer = initializer,
            Manifest = new SqliteIndexManifestService(factory, config, initializer),
            DocumentStore = new SqliteDocumentIndexStore(factory, config, new SqliteDocumentDeletionService()),
            SearchIndexReader = new SqliteSearchIndexReader(factory),
            SliceReader = new SqliteMarkdownSliceReader(factory),
            StatusReader = new SqliteIndexStatusReader(factory, config),
            VectorSearch = new SqliteVectorIndexService(factory),
            FullTextSearch = new SqliteFullTextSearchService(factory)
        };
    }
}
