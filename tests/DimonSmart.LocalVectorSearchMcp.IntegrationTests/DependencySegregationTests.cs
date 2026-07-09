using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Server;
using Microsoft.Extensions.DependencyInjection;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class DependencySegregationTests
{
    [Theory]
    [InlineData(typeof(KnowledgeBaseIndexer))]
    [InlineData(typeof(KnowledgeSearchService))]
    [InlineData(typeof(SemanticPointerReader))]
    [InlineData(typeof(KnowledgeMcpTools))]
    public void ApplicationService_DoesNotDependOnBroadRepository(Type serviceType)
    {
        Assert.DoesNotContain(
            serviceType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType.Name == "IKnowledgeRepository");
    }

    [Fact]
    public void StorageContracts_ResolveToSpecializedServices()
    {
        using var temp = new TemporaryDirectory();
        var config = new LocalVectorSearchMcpConfig
        {
            Storage = new StorageConfig { Path = Path.Combine(temp.Path, "index.db") },
            KnowledgeBase = new KnowledgeBaseConfig { Root = temp.Path }
        };
        var services = new ServiceCollection();
        services.AddLocalVectorSearchMcp(config);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SqliteSchemaInitializer>(provider.GetRequiredService<IIndexInitializer>());
        Assert.IsType<SqliteDocumentIndexStore>(provider.GetRequiredService<IDocumentIndexStore>());
        Assert.IsType<SqliteIndexStatusReader>(provider.GetRequiredService<IIndexStatusReader>());
        Assert.IsType<SqliteMarkdownSliceReader>(provider.GetRequiredService<IIndexedMarkdownSliceReader>());
        var searchReader = Assert.IsType<SqliteSearchIndexReader>(provider.GetRequiredService<IChunkSearchDocumentReader>());
        Assert.Same(searchReader, provider.GetRequiredService<ISearchIndexStateReader>());
        Assert.IsType<SqliteIndexManifestService>(provider.GetRequiredService<IIndexManifestService>());
        Assert.IsType<SqliteVectorIndexService>(provider.GetRequiredService<IVectorIndexService>());
        Assert.IsType<SqliteFullTextSearchService>(provider.GetRequiredService<IFullTextSearchService>());
    }
}
