using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Tests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class ConfigValidatorTests
{
    [Theory]
    [InlineData("embedding.model", 0)]
    [InlineData("embedding.model", 1)]
    [InlineData("embedding.dimensions", 0)]
    [InlineData("embedding.dimensions", -1)]
    [InlineData("embedding.timeoutSeconds", 0)]
    [InlineData("embedding.timeoutSeconds", -1)]
    [InlineData("search.semanticCandidatePoolSize", 0)]
    [InlineData("search.semanticCandidatePoolSize", -1)]
    [InlineData("search.lexicalCandidatePoolSize", 0)]
    [InlineData("search.lexicalCandidatePoolSize", -1)]
    [InlineData("search.maxResults", 0)]
    [InlineData("search.maxResults", -1)]
    [InlineData("search.rrfK", 0)]
    [InlineData("search.rrfK", -1)]
    public void Validate_RejectsInvalidEmbeddingAndSearchSettings(string path, int value)
    {
        using var temp = new TemporaryDirectory();
        var config = CreateConfig(temp.Path);
        config = path switch
        {
            "embedding.model" => config with
            {
                Embedding = config.Embedding with { Model = value == 0 ? "" : " " }
            },
            "embedding.dimensions" => config with
            {
                Embedding = config.Embedding with { Dimensions = value }
            },
            "embedding.timeoutSeconds" => config with
            {
                Embedding = config.Embedding with { TimeoutSeconds = value }
            },
            "search.semanticCandidatePoolSize" => config with
            {
                Search = config.Search with { SemanticCandidatePoolSize = value }
            },
            "search.lexicalCandidatePoolSize" => config with
            {
                Search = config.Search with { LexicalCandidatePoolSize = value }
            },
            "search.maxResults" => config with
            {
                Search = config.Search with { MaxResults = value }
            },
            "search.rrfK" => config with
            {
                Search = config.Search with { RrfK = value }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };

        var exception = Assert.Throws<ConfigurationException>(() => ConfigValidator.Validate(config));

        Assert.Contains(path, exception.Message);
    }

    private static LocalVectorSearchMcpConfig CreateConfig(string root) => new()
    {
        Storage = new StorageConfig { Path = Path.Combine(root, "index.db") },
        KnowledgeBase = new KnowledgeBaseConfig { Root = root }
    };
}
