using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Fakes;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ReadSliceTests
{
    [Fact]
    public async Task ReadSliceAsync_DocumentRootStartsAtFirstRealElement()
    {
        using var context = await CreateContextAsync("# Title\n\nText.");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer("document"), 20, 12_000, context.CancellationToken);

        Assert.Equal("document", slice.Pointer);
        Assert.Equal(new[] { "1", "1.p1" }, slice.Elements.Select(element => element.Pointer).ToArray());
        Assert.DoesNotContain(slice.Elements, element => element.Kind == MarkdownElementKind.Document);
        Assert.Equal("# Title\n\nText.", slice.Markdown);
        Assert.Null(slice.NextPointer);
    }

    [Fact]
    public async Task ReadSliceAsync_DocumentRootIncludesContentBeforeFirstHeading()
    {
        using var context = await CreateContextAsync("Introduction.\n\n# Chapter\n\nText.");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer("document"), 20, 12_000, context.CancellationToken);

        Assert.Equal(
            new[] { "p1", "1", "1.p1" },
            slice.Elements.Select(element => element.Pointer).ToArray());
        Assert.StartsWith("Introduction.", slice.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSliceAsync_DocumentRootIncludesFrontMatterInSourceOrder()
    {
        using var context = await CreateContextAsync("---\ntitle: Book\n---\n\nPreface.\n\n## Chapter");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer("document"), 20, 12_000, context.CancellationToken);

        Assert.Equal(
            new[] { "frontmatter", "p1", "1" },
            slice.Elements.Select(element => element.Pointer).ToArray());
        Assert.Equal(MarkdownElementKind.FrontMatter, slice.Elements[0].Kind);
    }

    [Fact]
    public async Task ReadSliceAsync_DocumentRootReadsDocumentWithoutHeadings()
    {
        using var context = await CreateContextAsync("First paragraph.\n\nSecond paragraph.");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer("document"), 20, 12_000, context.CancellationToken);

        Assert.Equal(
            new[] { "p1", "p2" },
            slice.Elements.Select(element => element.Pointer).ToArray());
    }

    [Fact]
    public async Task ReadSliceAsync_DocumentRootReturnsEmptySliceForEmptyDocument()
    {
        using var context = await CreateContextAsync("");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer("document"), 20, 12_000, context.CancellationToken);

        Assert.Equal("document", slice.Pointer);
        Assert.Empty(slice.Elements);
        Assert.Equal("", slice.Markdown);
        Assert.Null(slice.NextPointer);
        Assert.False(string.IsNullOrWhiteSpace(slice.SourceHash));
    }

    [Fact]
    public async Task ReadSliceAsync_DocumentRootMaxElementsDoesNotCountSyntheticDocument()
    {
        using var context = await CreateContextAsync("# Title\n\nOne.\n\nTwo.");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer("document"), 2, 12_000, context.CancellationToken);

        Assert.Equal(new[] { "1", "1.p1" }, slice.Elements.Select(element => element.Pointer).ToArray());
        Assert.Equal("1.p2", slice.NextPointer);
    }

    [Fact]
    public async Task ReadSliceAsync_DocumentRootMaxBytesContinuesWithoutLossOrDuplication()
    {
        using var context = await CreateContextAsync("Introduction.\n\n# Title\n\nBody.");

        var first = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer("document"), 20, 13, context.CancellationToken);
        var second = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md", new SemanticPointer(first.NextPointer!), 20, 12_000, context.CancellationToken);

        Assert.Equal(new[] { "p1" }, first.Elements.Select(element => element.Pointer).ToArray());
        Assert.Equal("1", first.NextPointer);
        Assert.Equal(new[] { "1", "1.p1" }, second.Elements.Select(element => element.Pointer).ToArray());
        Assert.Equal(
            new[] { "p1", "1", "1.p1" },
            first.Elements.Concat(second.Elements).Select(element => element.Pointer).ToArray());
    }

    [Fact]
    public async Task ReadSliceAsync_ReturnsNextPointerWhenMaxElementsCutsSlice()
    {
        using var context = await CreateContextAsync("# Title\n\nParagraph one.\n\nParagraph two.\n\nParagraph three.");

        var slice = await context.Services.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer("1.p1"), 2, 12_000, context.CancellationToken);

        Assert.Equal(2, slice.Elements.Count);
        Assert.Equal("1.p1", slice.Elements[0].Pointer);
        Assert.Equal("1.p2", slice.Elements[1].Pointer);
        Assert.Equal("1.p3", slice.NextPointer);
        Assert.Contains("Paragraph one", slice.Markdown);
        Assert.Contains("Paragraph two", slice.Markdown);
        Assert.DoesNotContain("Paragraph three", slice.Markdown);
    }

    [Fact]
    public async Task ReadSliceAsync_ContinuesFromNextPointer()
    {
        using var context = await CreateContextAsync("# Title\n\nParagraph one.\n\nParagraph two.\n\nParagraph three.");

        var first = await context.Services.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer("1.p1"), 2, 12_000, context.CancellationToken);
        var second = await context.Services.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer(first.NextPointer!), 2, 12_000, context.CancellationToken);

        Assert.Equal("1.p3", first.NextPointer);
        Assert.Single(second.Elements);
        Assert.Equal("1.p3", second.Elements[0].Pointer);
        Assert.Null(second.NextPointer);
        Assert.Contains("Paragraph three", second.Markdown);
    }

    [Fact]
    public async Task ReadSliceAsync_ReturnsNextPointerWhenMaxBytesCutsSlice()
    {
        using var context = await CreateContextAsync("# Title\n\nSmall one.\n\nSmall two.\n\nSmall three.");

        var slice = await context.Services.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer("1.p1"), 20, 16, context.CancellationToken);

        Assert.Single(slice.Elements);
        Assert.Equal("1.p1", slice.Elements[0].Pointer);
        Assert.Equal("1.p2", slice.NextPointer);
        Assert.Contains("Small one", slice.Markdown);
        Assert.DoesNotContain("Small two", slice.Markdown);
    }

    [Fact]
    public async Task ReadSliceAsync_ReturnsOversizedFirstElementInsteadOfNotFound()
    {
        using var context = await CreateContextAsync("# Title\n\nThis paragraph is intentionally longer than the byte limit.");

        var slice = await context.Services.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer("1.p1"), 20, 5, context.CancellationToken);

        Assert.Single(slice.Elements);
        Assert.Equal("1.p1", slice.Elements[0].Pointer);
        Assert.Contains("intentionally longer", slice.Markdown);
        Assert.Null(slice.NextPointer);
    }

    [Fact]
    public async Task ReadSliceAsync_OversizedFirstElementStillReturnsNextPointer()
    {
        using var context = await CreateContextAsync("# Title\n\nThis paragraph is intentionally longer than the byte limit.\n\nSecond paragraph.");

        var slice = await context.Services.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer("1.p1"), 20, 5, context.CancellationToken);

        Assert.Single(slice.Elements);
        Assert.Equal("1.p1", slice.Elements[0].Pointer);
        Assert.Equal("1.p2", slice.NextPointer);
        Assert.Contains("intentionally longer", slice.Markdown);
        Assert.DoesNotContain("Second paragraph", slice.Markdown);
    }

    [Fact]
    public async Task ReadSliceAsync_ThrowsNotFoundOnlyWhenPointerDoesNotExist()
    {
        using var context = await CreateContextAsync("# Title\n\nText.");

        var exception = await Assert.ThrowsAsync<SemanticPointerNotFoundException>(
            () => context.Services.SliceReader.ReadSliceAsync("notes.md", new SemanticPointer("1.p99"), 20, 12_000, context.CancellationToken));

        Assert.Contains("1.p99", exception.Message);
        Assert.Contains("notes.md", exception.Message);
    }

    [Fact]
    public async Task ReadSliceAsync_ThrowsNotFoundWhenPathDoesNotExist()
    {
        using var context = await CreateContextAsync("# Title\n\nText.");

        await Assert.ThrowsAsync<SemanticPointerNotFoundException>(
            () => context.Services.SliceReader.ReadSliceAsync("missing.md", new SemanticPointer("1.p1"), 20, 12_000, context.CancellationToken));
    }

    private static async Task<ReadSliceTestContext> CreateContextAsync(string markdown)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.md"), markdown, cancellationToken);
        var config = new LocalVectorSearchMcpConfig
        {
            Storage = new StorageConfig { Path = Path.Combine(temp.Path, ".local-vector-search-mcp", "index.db") },
            Embedding = new EmbeddingConfig { Model = "test", Dimensions = 3 },
            KnowledgeBase = new KnowledgeBaseConfig { Root = temp.Path }
        };
        var services = SqliteTestServices.Create(config);
        var indexer = new KnowledgeBaseIndexer(
            config,
            new MarkdownDocumentLoader(),
            new MarkdownElementParser(),
            new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()),
            new FakeEmbeddingProvider(),
            services.Initializer,
            services.DocumentStore,
            services.Manifest);
        await indexer.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), cancellationToken);
        return new ReadSliceTestContext(temp, services, cancellationToken);
    }

    private sealed record ReadSliceTestContext(
        TemporaryDirectory TemporaryDirectory,
        SqliteTestServices Services,
        CancellationToken CancellationToken) : IDisposable
    {
        public void Dispose() => TemporaryDirectory.Dispose();
    }
}
