using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Tests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class MarkdownChunkerConfigTests
{
    [Fact]
    public void MarkdownChunker_IncludesHeadingContext_WhenEnabled()
    {
        var chunks = BuildChunks("# Unique Heading\n\nParagraph text.", new ChunkingConfig { IncludeHeadingContext = true });

        Assert.Single(chunks);
        Assert.Contains("Unique Heading", chunks[0].EmbeddingText);
        Assert.Equal("Unique Heading", chunks[0].HeadingPath);
        Assert.DoesNotContain("Heading:", chunks[0].Text);
    }

    [Fact]
    public void MarkdownChunker_DoesNotIncludeHeadingContext_WhenDisabled()
    {
        var chunks = BuildChunks(
            "# Unique Heading For Embedding Test\n\nParagraph text without heading words.",
            new ChunkingConfig { IncludeHeadingContext = false });

        Assert.Single(chunks);
        Assert.DoesNotContain("Unique Heading For Embedding Test", chunks[0].EmbeddingText);
        Assert.Equal("Unique Heading For Embedding Test", chunks[0].HeadingPath);
        Assert.Contains("Paragraph text without heading words", chunks[0].EmbeddingText);
    }

    [Fact]
    public void MarkdownChunker_IncludesFrontMatter_WhenEnabled()
    {
        var chunks = BuildChunks(
            "---\ntitle: Unique FrontMatter Title\n---\n\n# Heading\n\nParagraph text.",
            new ChunkingConfig { IncludeFrontMatter = true });

        Assert.Contains("Unique FrontMatter Title", string.Join('\n', chunks.Select(chunk => chunk.Text)));
        Assert.Contains("Unique FrontMatter Title", string.Join('\n', chunks.Select(chunk => chunk.EmbeddingText)));
    }

    [Fact]
    public void MarkdownChunker_ExcludesFrontMatter_WhenDisabled()
    {
        var chunks = BuildChunks(
            "---\ntitle: Unique FrontMatter Title\n---\n\n# Heading\n\nParagraph text.",
            new ChunkingConfig { IncludeFrontMatter = false });

        Assert.DoesNotContain("Unique FrontMatter Title", string.Join('\n', chunks.Select(chunk => chunk.Text)));
        Assert.DoesNotContain("Unique FrontMatter Title", string.Join('\n', chunks.Select(chunk => chunk.EmbeddingText)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfigValidator_RejectsInvalidMaxChunkBytes(int value)
    {
        using var temp = new TemporaryDirectory();
        var config = TestConfig(temp.Path) with { Chunking = new ChunkingConfig { MaxChunkBytes = value } };

        var exception = Assert.Throws<ConfigurationException>(() => ConfigValidator.Validate(config));

        Assert.Contains("chunking.maxChunkBytes", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfigValidator_RejectsInvalidMaxElements(int value)
    {
        using var temp = new TemporaryDirectory();
        var config = TestConfig(temp.Path) with { Chunking = new ChunkingConfig { MaxElements = value } };

        var exception = Assert.Throws<ConfigurationException>(() => ConfigValidator.Validate(config));

        Assert.Contains("chunking.maxElements", exception.Message);
    }

    private static IReadOnlyList<MarkdownChunk> BuildChunks(string markdown, ChunkingConfig config)
    {
        var document = new MarkdownSourceDocument("notes.md", "c:/kb/notes.md", markdown, "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);
        return new MarkdownChunker(config, new EmbeddingTextBuilder()).BuildChunks(document, elements);
    }

    private static LocalVectorSearchMcpConfig TestConfig(string root) => new()
    {
        Storage = new StorageConfig { Path = Path.Combine(root, "index.db") },
        KnowledgeBase = new KnowledgeBaseConfig { Root = root }
    };
}
