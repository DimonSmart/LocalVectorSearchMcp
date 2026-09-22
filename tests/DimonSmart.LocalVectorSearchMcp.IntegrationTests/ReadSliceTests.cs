using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
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

    [Fact]
    public async Task ReadSliceAsync_PublicReadReturnsCanonicalFingerprintedPointers()
    {
        using var context = await CreateContextAsync(
            "# Title\n\nParagraph one.\n\nParagraph two.");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticAnchor(new SemanticPointer("document")),
            2,
            12_000,
            context.CancellationToken);

        Assert.Equal("document", slice.Pointer);
        Assert.Equal(2, slice.Elements.Count);
        Assert.All(
            slice.Elements,
            element => Assert.Matches(
                @"^[^~]+~[0-9a-f]{16}~[0-9a-f]{16}$",
                element.Pointer));
        Assert.NotNull(slice.NextPointer);
        Assert.Matches(@"^1\.p2~[0-9a-f]{16}~[0-9a-f]{16}$", slice.NextPointer!);
    }

    [Fact]
    public async Task ReadSliceAsync_PublicUnhashedPointerStillNavigates()
    {
        using var context = await CreateContextAsync("# Title\n\nTarget.");

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticAnchor(new SemanticPointer("1.p1")),
            20,
            12_000,
            context.CancellationToken);

        Assert.Matches(@"^1\.p1~[0-9a-f]{16}~[0-9a-f]{16}$", slice.Pointer);
        Assert.Equal(slice.Pointer, slice.Elements[0].Pointer);
        Assert.Equal("Target.", slice.Elements[0].Text);
    }

    [Fact]
    public async Task ReadSliceAsync_PublicFingerprintedPointerRelocatesAfterStructuralShift()
    {
        using var context = await CreateContextAsync(
            "# Title\n\nInserted.\n\nTarget.");
        var oldAnchor = new SemanticAnchor(
            new SemanticPointer("1.p1"),
            SemanticFingerprint.Compute("Target."));

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            oldAnchor,
            20,
            12_000,
            context.CancellationToken);

        Assert.Matches(@"^1\.p2~[0-9a-f]{16}~[0-9a-f]{16}$", slice.Pointer);
        Assert.Equal("Target.", slice.Elements[0].Text);
    }

    [Fact]
    public async Task ReadSliceAsync_PublicFingerprintedPointerRejectsAmbiguousRelocation()
    {
        using var context = await CreateContextAsync(
            "# Title\n\nChanged.\n\nTODO\n\nTODO");
        var oldAnchor = new SemanticAnchor(
            new SemanticPointer("1.p1"),
            SemanticFingerprint.Compute("TODO"));

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => context.Services.SliceReader.ReadSliceAsync(
                "notes.md",
                oldAnchor,
                20,
                12_000,
                context.CancellationToken));

        Assert.Contains(
            "multiple current elements",
            exception.Message,
            StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("## Ingredients\n\n- eggs\n- tomato\n- salt\n")]
    [InlineData("## Steps\n\n1. First\n2. Second\n3. Third\n")]
    [InlineData("- one\n  - child\n  - child 2\n- two\n")]
    [InlineData("- [ ] first\n- [x] second\n")]
    [InlineData("## Example\n\n```csharp\nConsole.WriteLine(42);\n```\n")]
    [InlineData("| Name | Amount |\n| --- | ---: |\n| Eggs | 2 |\n| Salt | 1 g |\n")]
    [InlineData("Text with **bold**, *italic*, `code`, [link](url) and ![image](image.png).\n\n> Important note\n")]
    public async Task ReadSliceAsync_DocumentRootPreservesExactMarkdownSource(string source)
    {
        using var context = await CreateContextAsync(source);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticPointer("document"),
            100,
            100_000,
            context.CancellationToken);

        Assert.Equal(source, slice.Markdown);
    }

    [Theory]
    [InlineData("- first\n- second\n- third\n", "p2", "- second\n")]
    [InlineData("1. first\n2. second\n3. third\n", "p2", "2. second\n")]
    [InlineData("- one\n  - child\n  - child 2\n- two\n", "p2", "  - child\n")]
    [InlineData("- [ ] first\n- [x] second\n", "p2", "- [x] second\n")]
    public async Task ReadSliceAsync_StartInsideContainerIncludesContainerSyntax(
        string source,
        string pointer,
        string expectedMarkdown)
    {
        using var context = await CreateContextAsync(source);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticPointer(pointer),
            1,
            100_000,
            context.CancellationToken);

        Assert.Equal(expectedMarkdown, slice.Markdown);
    }

    [Fact]
    public async Task ReadSliceAsync_PreservesCrLfBlankLinesAndTrailingSpaces()
    {
        const string source = "## Title\r\n\r\n- item  \r\n\r\nText.\r\n";
        using var context = await CreateContextAsync(source);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticPointer("document"),
            100,
            100_000,
            context.CancellationToken);

        Assert.Equal(source, slice.Markdown);
    }

    [Fact]
    public async Task ReadSliceAsync_PaginationConcatenatesBackToExactSource()
    {
        const string source = "- one\n- two\n\n<!-- raw comment -->\n\n- three\n";
        using var context = await CreateContextAsync(source);
        var pointer = new SemanticPointer("document");
        var reconstructed = new StringBuilder();

        for (var pageNumber = 0; pageNumber < 10; pageNumber++)
        {
            var page = await context.Services.SliceReader.ReadSliceAsync(
                "notes.md",
                pointer,
                1,
                100_000,
                context.CancellationToken);
            reconstructed.Append(page.Markdown);

            if (page.NextPointer is null)
            {
                Assert.Equal(source, reconstructed.ToString());
                return;
            }

            pointer = new SemanticPointer(page.NextPointer);
        }

        throw new Xunit.Sdk.XunitException("Pagination did not terminate.");
    }

    [Fact]
    public async Task ReadSliceAsync_MaxBytesCountsReturnedSourceMarkdown()
    {
        const string source = "- one\n- two\n- three\n";
        using var context = await CreateContextAsync(source);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticPointer("document"),
            100,
            6,
            context.CancellationToken);

        Assert.Equal("- one\n", slice.Markdown);
        Assert.Equal(6, Encoding.UTF8.GetByteCount(slice.Markdown));
        Assert.Equal("p2", slice.NextPointer);
        Assert.Single(slice.Elements);
    }

    [Fact]
    public async Task ReadSliceAsync_HeadingPointerReturnsExactSourceFromHeadingBoundary()
    {
        const string source = "Preface.\n\n## Recipe\n\n- item\n\n## Other\n\nKeep.\n";
        using var context = await CreateContextAsync(source);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticPointer("1"),
            2,
            100_000,
            context.CancellationToken);

        Assert.Equal("## Recipe\n\n- item\n\n", slice.Markdown);
        Assert.Equal("2", slice.NextPointer);
    }

    [Fact]
    public async Task ReadSliceAsync_DocumentRootPreservesRawSourceWithoutSemanticElements()
    {
        const string source = "<!-- raw comment -->\n";
        using var context = await CreateContextAsync(source);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticPointer("document"),
            20,
            1,
            context.CancellationToken);

        Assert.Empty(slice.Elements);
        Assert.Equal(source, slice.Markdown);
        Assert.Null(slice.NextPointer);
    }

    [Fact]
    public async Task ReadSliceAsync_UsesOneIndexedRevisionWhenFilesystemHasChanged()
    {
        const string indexedSource = "# Indexed\n\n- one\n- two\n";
        const string filesystemSource = "# Filesystem\n\nChanged.\n";
        using var context = await CreateContextAsync(indexedSource);
        await File.WriteAllTextAsync(
            Path.Combine(context.TemporaryDirectory.Path, "notes.md"),
            filesystemSource,
            context.CancellationToken);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticAnchor(new SemanticPointer("document")),
            100,
            100_000,
            context.CancellationToken);

        var indexedDocument = new MarkdownSourceDocument(
            "notes.md",
            "notes.md",
            indexedSource,
            "hash",
            DateTimeOffset.UtcNow);
        var expectedHeading = new MarkdownElementParser().Parse(indexedDocument)
            .Single(element => element.Pointer.Value == "1");

        Assert.Equal(indexedSource, slice.Markdown);
        Assert.Equal("# Indexed", slice.Elements[0].Text);
        Assert.Equal(
            SemanticAnchor.FromElement(expectedHeading).ToString(),
            slice.Elements[0].Pointer);
        Assert.Equal(
            StableHash.HashBytes(Encoding.UTF8.GetBytes(indexedSource)),
            slice.SourceHash);
    }

    [Fact]
    public async Task ReadSliceAsync_ReadModifyReplaceSectionPreservesUntouchedFormatting()
    {
        const string source =
            "## Recipe\n\n" +
            "Introduction.\n\n" +
            "### Ingredients\n\n" +
            "- 2 eggs\n" +
            "- 1 tomato\n" +
            "- salt\n\n" +
            "### Preparation\n\n" +
            "1. Cut tomato.\n" +
            "2. Beat eggs.\n" +
            "3. Fry everything.\n\n" +
            "## Other\n\n" +
            "Keep this section.\n";
        using var context = await CreateContextAsync(source);

        var slice = await context.Services.SliceReader.ReadSliceAsync(
            "notes.md",
            new SemanticAnchor(new SemanticPointer("1")),
            10,
            100_000,
            context.CancellationToken);
        var replacement = slice.Markdown.Replace(
            "Introduction.",
            "Updated introduction.",
            StringComparison.Ordinal);

        var mutations = CreateMutationService(context.TemporaryDirectory.Path);
        await mutations.PatchAsync(
            new PatchRequest(
                "notes.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    slice.Pointer,
                    replacement)]),
            context.CancellationToken);

        var actual = await File.ReadAllTextAsync(
            Path.Combine(context.TemporaryDirectory.Path, "notes.md"),
            context.CancellationToken);
        Assert.Equal(
            source.Replace(
                "Introduction.",
                "Updated introduction.",
                StringComparison.Ordinal),
            actual);
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


    private static WorkspaceMutationService CreateMutationService(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig
            {
                Root = root,
                AllowWrites = true
            }
        };

        return new WorkspaceMutationService(
            config,
            new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(),
            new MarkdownElementParser(),
            new ImmediateIndexSynchronizationScheduler(new NoOpSynchronizer()),
            new InMemoryIndexSynchronizationState());
    }

    private sealed record ReadSliceTestContext(
        TemporaryDirectory TemporaryDirectory,
        SqliteTestServices Services,
        CancellationToken CancellationToken) : IDisposable
    {
        public void Dispose() => TemporaryDirectory.Dispose();
    }

    private sealed class NoOpSynchronizer : IWorkspaceIndexSynchronizer
    {
        public Task<bool> ReconcileAsync(
            string relativePath,
            CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
