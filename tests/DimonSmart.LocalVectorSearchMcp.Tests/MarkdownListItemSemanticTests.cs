using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class MarkdownListItemSemanticTests
{
    private static IReadOnlyList<MarkdownElement> Parse(string source)
        => new MarkdownElementParser().Parse(
            new MarkdownSourceDocument("test.md", "test.md", source, "",
                DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("- A\n- B\n")]
    [InlineData("* A\n* B\n")]
    [InlineData("+ A\n+ B\n")]
    [InlineData("1. A\n2. B\n")]
    [InlineData("12) A\n13) B\n")]
    [InlineData("- [ ] A\n- [x] B\n")]
    public void AllCommonListMarkers_CreateSeparateAddressableItems(string source)
    {
        var elements = Parse(source);
        var items = elements.Where(x => x.Kind == MarkdownElementKind.ListItem).ToArray();
        Assert.Equal(["li1", "li2"], items.Select(x => x.Pointer.Value));
        Assert.All(items, item =>
        {
            Assert.NotNull(item.SourceMap);
            Assert.Equal(item.SelfHash, item.SubtreeHash);
            Assert.Equal(item.Text, source.Substring(item.SourceStart, item.SourceLength));
        });
        Assert.DoesNotContain(elements, x => x.Kind == MarkdownElementKind.Paragraph);
    }

    [Fact]
    public void NestedItems_HaveOwnHashesAndNoDuplicatedChildText()
    {
        const string source = "## Food\n\n- Vegetables\n  - Onion\n  - Tomato\n- Meat\n";
        var items = Parse(source).Where(x => x.Kind == MarkdownElementKind.ListItem).ToArray();
        Assert.Equal(["1.li1", "1.li1.li1", "1.li1.li2", "1.li2"],
            items.Select(x => x.Pointer.Value));
        Assert.Equal("- Vegetables", items[0].Text);
        Assert.DoesNotContain("Onion", items[0].Text);
        Assert.Equal("1.li1", items[1].SourceMap?.ParentPointer);
        Assert.Equal(1, items[1].SourceMap?.Depth);
        Assert.NotEqual(items[0].SelfHash, items[0].SubtreeHash);
    }

    [Fact]
    public void ChildChanges_DoNotChangeParentOwnHash()
    {
        var first = Parse("- Parent\n  - Child\n")[1];
        var changed = Parse("- Parent\n  - Different\n")[1];
        var alone = Parse("- Parent\n")[1];
        Assert.Equal(first.SelfHash, changed.SelfHash);
        Assert.Equal(first.SelfHash, alone.SelfHash);
        Assert.NotEqual(first.SubtreeHash, changed.SubtreeHash);
        Assert.NotEqual(first.SubtreeHash, alone.SubtreeHash);
    }

    [Fact]
    public void Quotes_AreOpaque_AndDoNotProduceSubelements()
    {
        const string source = "> # Not heading\n> - Not list\n>   - Nested\n> ```csharp\n> code\n> ```\n\nOutside.\n";
        var elements = Parse(source).Where(x => x.Kind != MarkdownElementKind.Document).ToArray();
        Assert.Equal(2, elements.Length);
        Assert.Equal(MarkdownElementKind.BlockQuote, elements[0].Kind);
        Assert.Equal("q1", elements[0].Pointer.Value);
        Assert.Equal(MarkdownElementKind.Paragraph, elements[1].Kind);
        Assert.Equal(elements[0].SelfHash, elements[0].SubtreeHash);
        Assert.Equal(source[..source.IndexOf("\n\n", StringComparison.Ordinal)], elements[0].Text);
        Assert.DoesNotContain(elements, x => x.Kind == MarkdownElementKind.Heading);
        Assert.DoesNotContain(elements, x => x.Kind == MarkdownElementKind.ListItem);
    }

    [Fact]
    public void PointerGrammar_RecognizesNestedItemsAndAtomicQuotes()
    {
        Assert.True(SemanticPointerParser.IsValid("li1.li2"));
        Assert.True(SemanticPointerParser.IsValid("1.2.li3.li1"));
        Assert.True(SemanticPointerParser.IsValid("1.q2"));
        Assert.False(SemanticPointerParser.IsValid("li1.p1"));
        Assert.False(SemanticPointerParser.IsValid("q1.li1"));
        Assert.Equal(SemanticPointerKind.ListItem,
            SemanticPointerParser.GetKind(new SemanticPointer("1.li2.li1")));
        Assert.Equal("1.2", SemanticPointerParser.GetContainingSectionPointer(
            new SemanticPointer("1.2.li3.li1"))?.Value);
        Assert.Null(SemanticPointerParser.GetContainingSectionPointer(
            new SemanticPointer("li1.li2")));
        Assert.Equal("1.li2", SemanticPointerParser.GetParentListItemPointer(
            new SemanticPointer("1.li2.li1"))?.Value);
    }

    [Fact]
    public void SuppressedLegacyPositions_AreNotReused()
    {
        var elements = Parse("First.\n\n- Inside\n\nSecond.\n");
        Assert.Contains(elements, x => x.Pointer.Value == "p1");
        Assert.Contains(elements, x => x.Pointer.Value == "p3");
        Assert.DoesNotContain(elements, x => x.Pointer.Value == "p2");
        Assert.Contains("p2", elements[0].ReservedPointers!);
    }

    [Fact]
    public void FencedCodeMarkers_DoNotCreateListItems()
    {
        const string source = "```text\n- not a list\n1. not a list\n```\n";
        var elements = Parse(source);
        Assert.Single(elements, x => x.Kind == MarkdownElementKind.CodeBlock);
        Assert.DoesNotContain(elements, x => x.Kind == MarkdownElementKind.ListItem);
    }

    [Fact]
    public void QuoteInsideList_IsPartOfItemNotASeparateElement()
    {
        var elements = Parse("- Parent\n  > - Inside quote\n");
        Assert.Single(elements, x => x.Kind == MarkdownElementKind.ListItem);
        Assert.DoesNotContain(elements, x => x.Kind == MarkdownElementKind.BlockQuote);
    }


    [Fact]
    public void ParentOwnParagraphAfterChild_RemainsOwnedByParent()
    {
        const string source = "- Parent\n\n  - Child\n\n  Own paragraph after the child.\n";
        var items = Parse(source).Where(x => x.Kind == MarkdownElementKind.ListItem).ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("li1.li1", items[1].Pointer.Value);
        Assert.Contains("Own paragraph after the child.", items[0].Text);
        Assert.DoesNotContain("Child", items[0].Text);
        Assert.NotEqual(items[0].SelfHash, items[0].SubtreeHash);
    }

    [Fact]
    public void Chunker_IndexesEachChildAndQuoteExactlyOnce()
    {
        const string source = "# Title\n\n- Parent\n  - Child\n\n> - Quoted child\n";
        var document = new MarkdownSourceDocument("a.md", "a.md", source, "",
            DateTimeOffset.UtcNow);
        var chunks = new MarkdownChunker(
            new ChunkingConfig { MaxElements = 20, MaxChunkBytes = 10_000 },
            new DimonSmart.LocalVectorSearchMcp.Core.Embeddings.EmbeddingTextBuilder())
            .BuildChunks(document, Parse(source));
        var indexed = string.Join("\n", chunks.Select(x => x.Text));
        Assert.Equal(1, indexed.Split("- Child").Length - 1);
        Assert.Equal(1, indexed.Split("> - Quoted child").Length - 1);
    }
}
