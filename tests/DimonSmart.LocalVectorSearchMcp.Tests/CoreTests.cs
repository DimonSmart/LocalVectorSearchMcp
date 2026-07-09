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
using DimonSmart.LocalVectorSearchMcp.Infrastructure;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Tests.Helpers;
using System.Text.Json;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class CoreTests
{
    [Fact]
    public void SemanticPointerParser_AcceptsAndClassifiesPointers()
    {
        Assert.True(SemanticPointerParser.IsValid("frontmatter"));
        Assert.True(SemanticPointerParser.IsValid("2.1.p3"));
        Assert.True(SemanticPointerParser.IsValid("code1"));
        Assert.False(SemanticPointerParser.IsValid("../x"));
        Assert.Equal(SemanticPointerKind.Paragraph, SemanticPointerParser.GetKind(new SemanticPointer("2.1.p3")));
        Assert.Equal(new SemanticPointer("2.1"), SemanticPointerParser.GetContainingSectionPointer(new SemanticPointer("2.1.p3")));
    }

    [Fact]
    public void MarkdownTextNormalizer_NormalizesLineEndingsAndBlankLines()
    {
        var normalized = MarkdownTextNormalizer.Normalize("a  \r\n\r\n\r\nb\r\n");
        Assert.Equal("a\n\nb\n", normalized);
    }

    [Fact]
    public void MarkdownElementParser_ParsesFrontMatterHeadingsParagraphsAndCode()
    {
        var doc = new MarkdownSourceDocument("docs/a.md", "c:/kb/docs/a.md", "---\ntitle: A\n---\n# One\nText\n\n## Two\n```csharp\nvar x = 1;\n```\n", "h", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(doc);

        Assert.Contains(elements, e => e.Kind == MarkdownElementKind.FrontMatter && e.Pointer.Value == "frontmatter");
        Assert.Contains(elements, e => e.Kind == MarkdownElementKind.Heading && e.Pointer.Value == "1");
        Assert.Contains(elements, e => e.Kind == MarkdownElementKind.Paragraph && e.Pointer.Value == "1.p1");
        Assert.Contains(elements, e => e.Kind == MarkdownElementKind.CodeBlock && e.Pointer.Value == "1.1.code1");
    }

    [Fact]
    public void MarkdownChunker_BuildsStableChunksWithHeadingContext()
    {
        var doc = new MarkdownSourceDocument("a.md", "c:/kb/a.md", "# H\nText\n", "h", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(doc);
        var chunker = new MarkdownChunker(new ChunkingConfig { MaxChunkBytes = 4096, MaxElements = 20 }, new EmbeddingTextBuilder());

        var chunks = chunker.BuildChunks(doc, elements);

        Assert.Single(chunks);
        Assert.Contains("Path: a.md", chunks[0].EmbeddingText);
        Assert.Contains("Heading: H", chunks[0].EmbeddingText);
        Assert.Equal(chunks[0].EmbeddingTextHash, StableHash.HashText(chunks[0].EmbeddingText));
    }

    [Fact]
    public void HybridRanker_DeduplicatesAndBoostsOverlappingResults()
    {
        var fused = HybridRanker.Fuse([1, 2], [2, 3], 60, 10);

        Assert.Equal(2, fused[0].ChunkId);
        Assert.Equal(3, fused.Count);
    }

    [Fact]
    public void WireEnums_DeserializeCaseInsensitivelyAndSerializeLowercase()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var search = JsonSerializer.Deserialize<SearchRequest>("""{"query":"q","mode":"SEMANTIC"}""", options);
        var reindex = JsonSerializer.Deserialize<ReindexRequest>("""{"scope":"ALL","force":true}""", options);
        var result = new SearchResultItem("a.md", "1.p1", "a.md::1.p1", 1, SearchMode.Lexical, null, "text", new ReadHint("a.md", "1.p1", 20, 12000));

        var json = JsonSerializer.Serialize(result, options);

        Assert.Equal(SearchMode.Semantic, search?.Mode);
        Assert.Equal(ReindexScope.All, reindex?.Scope);
        Assert.Contains("\"searchMode\":\"lexical\"", json);
    }

    [Fact]
    public void ConfigLoader_AcceptsSearchDefaultModeCaseInsensitively()
    {
        using var temp = new TemporaryDirectory();
        var configPath = Path.Combine(temp.Path, "config.yml");
        File.WriteAllText(configPath, $"""
            storage:
              path: index.db
            search:
              defaultMode: HYBRID
            knowledgeBase:
              root: {temp.Path.Replace("\\", "/")}
            """);

        var config = LocalVectorSearchConfigLoader.Load(["--config", configPath]);

        Assert.Equal(SearchMode.Hybrid, config.Search.DefaultMode);
    }

    [Fact]
    public void ConfigLoader_RejectsLegacyKnowledgeBasesList()
    {
        using var temp = new TemporaryDirectory();
        var configPath = Path.Combine(temp.Path, "config.yml");
        File.WriteAllText(configPath, """
            knowledgeBases:
              - name: old
                root: .
            """);

        var exception = Assert.Throws<ConfigurationException>(
            () => LocalVectorSearchConfigLoader.Load(["--config", configPath]));

        Assert.Contains("Use singular knowledgeBase", exception.Message);
    }

    [Fact]
    public void PathGuard_RejectsTraversalAndAcceptsRelativePath()
    {
        using var temp = new TemporaryDirectory();
        var config = TestConfig(temp.Path);
        var guard = new KnowledgeBasePathGuard(config);

        Assert.Equal("docs/a.md", guard.ValidateRelativePath("docs/a.md"));
        Assert.Throws<KnowledgeBaseAccessException>(() => guard.ValidateRelativePath("../a.md"));
        Assert.Throws<KnowledgeBaseAccessException>(() => guard.ValidateRelativePath(Path.GetFullPath("a.md")));
    }

    [Fact]
    public void ConfigValidator_RejectsRemoteEndpointByDefault()
    {
        using var temp = new TemporaryDirectory();
        var config = TestConfig(temp.Path) with { Embedding = new EmbeddingConfig { Endpoint = "https://example.com/v1" } };

        Assert.Throws<ConfigurationException>(() => ConfigValidator.Validate(config));
    }

    private static LocalVectorSearchMcpConfig TestConfig(string root) => new()
    {
        Storage = new StorageConfig { Path = Path.Combine(root, "index.db") },
        KnowledgeBase = new KnowledgeBaseConfig { Root = root }
    };
}
