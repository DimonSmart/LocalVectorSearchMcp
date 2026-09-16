using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class MarkdownHardeningTests
{
    [Fact]
    public void Chunker_SeparatesRepeatedHeadingTexts()
    {
        const string source = "# Book\n\n## Example\n\nText A.\n\n## Example\n\nText B.\n";
        var (elements, chunks) = ParseAndChunk(source);

        var body = elements.Where(element => element.Kind == MarkdownElementKind.Paragraph).ToList();
        Assert.Equal(["1.1", "1.2"], body.Select(element => element.SectionPointer.Value));
        Assert.DoesNotContain(chunks, chunk => chunk.Text.Contains("Text A.") && chunk.Text.Contains("Text B."));
    }

    [Fact]
    public void Chunker_SeparatesRepeatedNestedHeadingPaths()
    {
        const string source = "## Same\n\n### Details\n\nA\n\n## Same\n\n### Details\n\nB\n";
        var (elements, chunks) = ParseAndChunk(source);

        var body = elements.Where(element => element.Kind == MarkdownElementKind.Paragraph).ToList();
        Assert.Equal(["1.1", "2.1"], body.Select(element => element.SectionPointer.Value));
        Assert.DoesNotContain(chunks, chunk => chunk.Text.Contains("A") && chunk.Text.Contains("B"));
    }

    [Fact]
    public void Parser_UsesHeadingTreeForSkippedLevels()
    {
        const string source = "# A\n\n### X\n\ntext X\n\n## Y\n\ntext Y\n\n#### Z\n\ntext Z\n";
        var (elements, chunks) = ParseAndChunk(source);
        var headings = elements.Where(element => element.Kind == MarkdownElementKind.Heading).ToList();

        Assert.Equal(["1", "1.1", "1.2", "1.2.1"], headings.Select(element => element.Pointer.Value));
        Assert.Equal(headings.Count, headings.Select(element => element.Pointer.Value).Distinct().Count());
        Assert.DoesNotContain(chunks, chunk => chunk.Text.Contains("text X") && chunk.Text.Contains("text Y"));
    }

    [Fact]
    public void Chunker_SeparatesDocumentRootFromHeadingSection()
    {
        const string source = "Paragraph before headings.\n\n# Heading\n\nParagraph inside heading.\n";
        var (elements, chunks) = ParseAndChunk(source);
        var body = elements.Where(element => element.Kind == MarkdownElementKind.Paragraph).ToList();

        Assert.Equal("document", body[0].SectionPointer.Value);
        Assert.Equal("1", body[1].SectionPointer.Value);
        Assert.DoesNotContain(chunks, chunk =>
            chunk.Text.Contains("Paragraph before headings.")
            && chunk.Text.Contains("Paragraph inside heading."));
    }

    [Fact]
    public void SemanticPointerParser_AcceptsDocumentPointer()
    {
        Assert.True(SemanticPointerParser.IsValid("document"));
        Assert.Equal(
            SemanticPointerKind.Document,
            SemanticPointerParser.GetKind(SemanticPointerParser.Parse("document")));
        Assert.Null(SemanticPointerParser.GetContainingSectionPointer(new SemanticPointer("document")));
    }

    [Theory]
    [InlineData(PatchOperationKind.InsertBefore)]
    [InlineData(PatchOperationKind.InsertAfter)]
    public void SourcePatcher_DocumentInsertion_FillsEmptyDocumentWithoutExtraBlankLines(PatchOperationKind kind)
    {
        var document = new MarkdownSourceDocument("a.md", "a.md", "", "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);

        var result = MarkdownSourcePatcher.Apply(
            "",
            elements,
            [new PatchOperation(kind, "document", "# Chapter 1\r\n\r\nText.")]);

        Assert.Equal("# Chapter 1\n\nText.", result);
    }

    [Fact]
    public void SourcePatcher_DocumentInsertion_WrapsNonEmptySourceAsMarkdownBlocks()
    {
        const string source = "Existing.\r\n";
        var document = new MarkdownSourceDocument("a.md", "a.md", source, "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);

        var before = MarkdownSourcePatcher.Apply(
            source,
            elements,
            [new PatchOperation(PatchOperationKind.InsertBefore, "document", "Before.")]);
        var after = MarkdownSourcePatcher.Apply(
            source,
            elements,
            [new PatchOperation(PatchOperationKind.InsertAfter, "document", "After.")]);

        Assert.Equal("Before.\r\n\r\nExisting.\r\n", before);
        Assert.Equal("Existing.\r\n\r\n\r\nAfter.", after);
    }

    [Theory]
    [InlineData(PatchOperationKind.Replace)]
    [InlineData(PatchOperationKind.Delete)]
    public void SourcePatcher_RejectsDestructiveDocumentOperations(PatchOperationKind kind)
    {
        var document = new MarkdownSourceDocument("a.md", "a.md", "Text.\n", "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);

        var exception = Assert.Throws<WorkspaceMutationException>(() => MarkdownSourcePatcher.Apply(
            document.Markdown,
            elements,
            [new PatchOperation(kind, "document", kind == PatchOperationKind.Replace ? "Replacement." : null)]));

        Assert.Contains("document pointer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (IReadOnlyList<MarkdownElement> Elements, IReadOnlyList<MarkdownChunk> Chunks) ParseAndChunk(string source)
    {
        var document = new MarkdownSourceDocument("a.md", "a.md", source, "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);
        var chunks = new MarkdownChunker(
            new ChunkingConfig { MaxElements = 100, MaxChunkBytes = 100_000 },
            new EmbeddingTextBuilder()).BuildChunks(document, elements);
        return (elements, chunks);
    }
}
