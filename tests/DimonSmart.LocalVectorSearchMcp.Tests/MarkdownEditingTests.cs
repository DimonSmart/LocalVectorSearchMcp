using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Tests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class MarkdownEditingTests
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    public async Task LoaderAndParser_PreserveExactSourceAndSpans(string eol, bool bom)
    {
        using var temp = new TemporaryDirectory();
        var source = string.Join(eol,
            "---",
            "title: Test",
            "---",
            "# Heading",
            "Paragraph  ",
            "",
            "- list item",
            "",
            "> quotation",
            "",
            "```csharp",
            "var x = 1;",
            "```",
            "");
        var bytes = new UTF8Encoding(bom).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(source))
            .ToArray();
        var path = Path.Combine(temp.Path, "source.md");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);

        var loader = new MarkdownDocumentLoader();
        var document = await loader.LoadFileAsync(
            new KnowledgeBaseConfig { Root = temp.Path },
            "source.md",
            TestContext.Current.CancellationToken);
        var elements = new MarkdownElementParser().Parse(document)
            .Where(element => element.Kind != MarkdownElementKind.Document)
            .ToList();

        Assert.Equal(source, document.Markdown);
        Assert.Equal(bom, document.HasUtf8Bom);
        Assert.All(elements, element =>
            Assert.Equal(element.Text, source.Substring(element.SourceStart, element.SourceLength)));
        Assert.Contains(elements, element => element.Kind == MarkdownElementKind.Heading);
        Assert.Contains(elements, element => element.Kind == MarkdownElementKind.CodeBlock);
    }

    [Fact]
    public void SourceHash_DistinguishesLineEndingsWhitespaceAndBom()
    {
        var lf = Encoding.UTF8.GetBytes("text\n");
        var crlf = Encoding.UTF8.GetBytes("text\r\n");
        var spaced = Encoding.UTF8.GetBytes("text  \n");
        var bom = Encoding.UTF8.Preamble.ToArray().Concat(lf).ToArray();

        var hashes = new[]
        {
            Core.Storage.StableHash.HashBytes(lf),
            Core.Storage.StableHash.HashBytes(crlf),
            Core.Storage.StableHash.HashBytes(spaced),
            Core.Storage.StableHash.HashBytes(bom)
        };

        Assert.Equal(4, hashes.Distinct().Count());
    }

    [Fact]
    public async Task Loader_KeepsNormalizedContentHashSeparateFromExactSourceHash()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "lf.md"),
            "text\n",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "crlf.md"),
            "text\r\n",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        var loader = new MarkdownDocumentLoader();
        var config = new KnowledgeBaseConfig { Root = temp.Path };

        var lf = await loader.LoadFileAsync(config, "lf.md", TestContext.Current.CancellationToken);
        var crlf = await loader.LoadFileAsync(config, "crlf.md", TestContext.Current.CancellationToken);

        Assert.Equal(lf.ContentHash, crlf.ContentHash);
        Assert.NotEqual(lf.SourceHash, crlf.SourceHash);
    }

    [Fact]
    public void SourcePatcher_SupportsAllOperationsAndPreservesUntouchedCrLf()
    {
        const string source = "# One\r\n\r\nFirst paragraph.  \r\n\r\nSecond paragraph.\r\n";
        var document = new MarkdownSourceDocument("a.md", "a.md", source, "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);

        var patched = MarkdownSourcePatcher.Apply(
            source,
            elements,
            [
                new PatchOperation(PatchOperationKind.InsertBefore, "1.p1", "Before."),
                new PatchOperation(PatchOperationKind.Replace, "1.p2", "Replacement."),
                new PatchOperation(PatchOperationKind.InsertAfter, "1", "After heading.")
            ]);

        Assert.Contains("# One\r\n\r\nAfter heading.", patched);
        Assert.Contains("Before.\r\n\r\nFirst paragraph.  ", patched);
        Assert.Contains("Replacement.", patched);
        Assert.DoesNotContain("Second paragraph.", patched);
        Assert.DoesNotContain("\n", patched.Replace("\r\n", ""));
    }

    [Fact]
    public void SourcePatcher_RejectsMissingAndConflictingPointersAtomically()
    {
        const string source = "# One\n\nParagraph.\n";
        var document = new MarkdownSourceDocument("a.md", "a.md", source, "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);

        Assert.Throws<WorkspaceMutationException>(() => MarkdownSourcePatcher.Apply(
            source,
            elements,
            [new PatchOperation(PatchOperationKind.Delete, "9.p1")]));
        Assert.Throws<WorkspaceMutationException>(() => MarkdownSourcePatcher.Apply(
            source,
            elements,
            [
                new PatchOperation(PatchOperationKind.Delete, "1.p1"),
                new PatchOperation(PatchOperationKind.Replace, "1.p1", "changed")
            ]));
        Assert.Equal("# One\n\nParagraph.\n", source);
    }

    [Fact]
    public void Chunker_NeverCrossesHeadingBoundary()
    {
        const string source = "# One\n\nParagraph A.\n\n## Two\n\nParagraph B.\n";
        var document = new MarkdownSourceDocument("a.md", "a.md", source, "hash", DateTimeOffset.UtcNow);
        var elements = new MarkdownElementParser().Parse(document);
        var chunks = new MarkdownChunker(
            new ChunkingConfig { MaxElements = 20, MaxChunkBytes = 10000 },
            new Core.Embeddings.EmbeddingTextBuilder()).BuildChunks(document, elements);

        Assert.Equal(2, chunks.Count);
        Assert.DoesNotContain(chunks, chunk =>
            chunk.Text.Contains("Paragraph A.") && chunk.Text.Contains("Paragraph B."));
    }
}
