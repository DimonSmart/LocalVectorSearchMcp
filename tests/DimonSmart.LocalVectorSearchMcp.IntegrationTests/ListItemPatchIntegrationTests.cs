using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ListItemPatchIntegrationTests
{
    [Fact]
    public async Task ReplaceOwnText_OnNestedLeaf_PreservesItsContainerAndSiblings()
    {
        const string source = "- Parent\n  - Child\n    - Grandchild\n  - Leaf\n- Sibling\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var leaf = Read(temp.Path).Single(x => x.Pointer.Value == "li1.li2");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(leaf).ToString(), "  - Renamed leaf")]),
            TestContext.Current.CancellationToken);

        Assert.Equal("- Parent\n  - Child\n    - Grandchild\n  - Renamed leaf\n- Sibling\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSubtree_OnNestedItem_ReplacesOnlyThatSubtree()
    {
        const string source = "- Parent\n  - Child\n    - Grandchild\n  - Leaf\n- Sibling\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var child = Read(temp.Path).Single(x => x.Pointer.Value == "li1.li1");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceSubtree,
                SemanticAnchor.FromElement(child).ToString(),
                "  - Renamed child\n    - New grandchild")]),
            TestContext.Current.CancellationToken);

        Assert.Equal("- Parent\n  - Renamed child\n    - New grandchild\n  - Leaf\n- Sibling\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceOwnText_OnNestedParent_PreservesDescendants()
    {
        const string source = "- Parent\n  - Child\n    - Grandchild\n  - Leaf\n- Sibling\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var child = Read(temp.Path).Single(x => x.Pointer.Value == "li1.li1");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(child).ToString(), "  - Renamed child")]),
            TestContext.Current.CancellationToken);

        Assert.Equal("- Parent\n  - Renamed child\n    - Grandchild\n  - Leaf\n- Sibling\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InsertAfter_OnNestedItem_CreatesOneSiblingInItsContainer()
    {
        const string source = "- Parent\n  - First\n  - Last\n- Sibling\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var first = Read(temp.Path).Single(x => x.Pointer.Value == "li1.li1");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.InsertAfter,
                SemanticAnchor.FromElement(first).ToString(), "  - Inserted")]),
            TestContext.Current.CancellationToken);

        Assert.Equal("- Parent\n  - First\n  - Inserted\n  - Last\n- Sibling\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("- A\n- B\n- C\n", "li2", "- A\n- C\n")]
    [InlineData("* A\n* B\n", "li1", "* B\n")]
    [InlineData("+ A\n", "li1", "")]
    [InlineData("1. A\n2. B\n", "li2", "1. A\n")]
    [InlineData("1) A\n2) B", "li1", "2) B")]
    [InlineData("- Parent\n  - Child\n  - Other\n", "li1.li1", "- Parent\n  - Other\n")]
    [InlineData("- Parent\n  - Child\n- Other\n", "li1", "- Other\n")]
    [InlineData("- A\r\n- B\r\n", "li1", "- B\r\n")]
    [InlineData("- A\n- B", "li2", "- A\n")]
    public async Task Delete_RemovesWholePhysicalItem(
        string source, string pointer, string expected)
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var element = Read(temp.Path).Single(x => x.Pointer.Value == pointer);
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(
                PatchOperationKind.Delete, SemanticAnchor.FromElement(element).ToString())]),
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSubtree_ExchangesCompleteNestedSubtree()
    {
        const string source = "- Vegetables\n  - Onion\n  - Tomato\n- Meat\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var element = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(
                PatchOperationKind.ReplaceSubtree,
                SemanticAnchor.FromElement(element).ToString(),
                "- Fruits\n  - Apple\n  - Pear")]),
            TestContext.Current.CancellationToken);
        Assert.Equal("- Fruits\n  - Apple\n  - Pear\n- Meat\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PatchOperationKind.ReplaceElement)]
    [InlineData(PatchOperationKind.Replace)]
    public async Task ReplaceOwnText_PreservesTwoNestedChildren(PatchOperationKind operation)
    {
        const string source = "# Шашлык\n\n## Ингредиенты\n\n- **Мясо:** свиная шея.\n- **Овощи:** сладкий перец, лук и грибы.\n  - Лук удобно нанизывать отдельно.\n  - Помидоры черри лучше готовить отдельно.\n- **Инвентарь:** шампуры и мангал.\n";
        const string expected = "# Шашлык\n\n## Ингредиенты\n\n- **Мясо:** свиная шея.\n- **Овощи и зелень:** перец, лук, грибы и укроп.\n  - Лук удобно нанизывать отдельно.\n  - Помидоры черри лучше готовить отдельно.\n- **Инвентарь:** шампуры и мангал.\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var parent = Read(temp.Path).Single(x => x.Pointer.Value == "1.1.li2");
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(operation,
                SemanticAnchor.FromElement(parent).ToString(),
                "- **Овощи и зелень:** перец, лук, грибы и укроп.")]),
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        var items = Read(temp.Path).Where(x => x.Kind == MarkdownElementKind.ListItem).ToArray();
        Assert.Equal(["1.1.li1", "1.1.li2", "1.1.li2.li1", "1.1.li2.li2", "1.1.li3"],
            items.Select(x => x.Pointer.Value));
        Assert.Equal("1.1.li2", items[2].SourceMap?.ParentPointer);
        Assert.Equal("1.1.li2", items[3].SourceMap?.ParentPointer);
    }

    [Fact]
    public async Task ReplaceOwnText_AfterChildChanges_UsesCurrentChildren()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, "- Parent\n  - Old child\n",
            TestContext.Current.CancellationToken);
        var oldParent = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await File.WriteAllTextAsync(file, "- Parent\n  - Updated child\n",
            TestContext.Current.CancellationToken);
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(
                PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(oldParent).ToString(), "- Renamed parent")]),
            TestContext.Current.CancellationToken);
        Assert.Equal("- Renamed parent\n  - Updated child\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("- Parent\n  - First\n    - Second\n      - Third\n- End\n",
        "- New parent\n  - First\n    - Second\n      - Third\n- End\n")]
    [InlineData("- [ ] Parent\n  - Child\n- End\n",
        "- [x] Parent\n  - Child\n- End\n")]
    [InlineData("12) Parent\n    - Child\n13) End\n",
        "12) New parent\n    - Child\n13) End\n")]
    [InlineData("- Parent\n\n  First paragraph.\n\n  - Child\n\n  Last paragraph.\n- End\n",
        "- New parent\n\n  - Child\n\n  Last paragraph.\n- End\n")]
    [InlineData("- Parent\n\n  ```text\n  code\n  ```\n\n  - Child\n- End\n",
        "- New parent\n\n  - Child\n- End\n")]
    public async Task ReplaceOwnText_PreservesNestedSubtreesAndOwnSuffix(
        string source, string expected)
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var parent = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        var marker = source.Split(['\r', '\n'], 2)[0];
        var replacement = marker.StartsWith("12)", StringComparison.Ordinal)
            ? "12) New parent" : marker.StartsWith("- [ ]", StringComparison.Ordinal)
                ? "- [x] Parent" : "- New parent";
        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(parent).ToString(), replacement)]),
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceOwnText_PreservesCrLfAndBom()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        var bytes = new System.Text.UTF8Encoding(true).GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes("- Parent\r\n  - Child\r\n- Other\r\n"))
            .ToArray();
        await File.WriteAllBytesAsync(file, bytes, TestContext.Current.CancellationToken);
        var parent = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(parent).ToString(), "- Updated")]),
            TestContext.Current.CancellationToken);
        var expected = new System.Text.UTF8Encoding(true).GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes("- Updated\r\n  - Child\r\n- Other\r\n"))
            .ToArray();
        Assert.Equal(expected, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceOwnText_RejectsAdditionalChildrenWithoutWriting()
    {
        const string source = "- Parent\n  - Child\n- Other\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var parent = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
                [new PatchOperation(PatchOperationKind.ReplaceElement,
                    SemanticAnchor.FromElement(parent).ToString(),
                    "- New parent\n  - Replacement child")]),
                TestContext.Current.CancellationToken));
        Assert.Equal(source, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceOwnText_WithSiblingEdit_RemainsAtomic()
    {
        const string source = "- Parent\n  - Child\n- Other\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var items = Read(temp.Path);
        var parent = items.Single(x => x.Pointer.Value == "li1");
        var sibling = items.Single(x => x.Pointer.Value == "li2");
        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [
                new PatchOperation(PatchOperationKind.ReplaceElement,
                    SemanticAnchor.FromElement(parent).ToString(), "- Updated"),
                new PatchOperation(PatchOperationKind.ReplaceElement,
                    SemanticAnchor.FromElement(sibling).ToString(), "- Renamed")
            ]), TestContext.Current.CancellationToken);
        Assert.Equal("- Updated\n  - Child\n- Renamed\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSubtree_RejectsStaleChildRevision()
    {
        const string original = "- Parent\n  - First\n";
        const string latest = "- Parent\n  - Changed child\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, original, TestContext.Current.CancellationToken);
        var staleParent = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await File.WriteAllTextAsync(file, latest, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<SemanticAnchorConflictException>(() =>
            CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
                [new PatchOperation(PatchOperationKind.ReplaceSubtree,
                    SemanticAnchor.FromElement(staleParent).ToString(),
                    "- Updated\n  - New child")]),
                TestContext.Current.CancellationToken));
        Assert.Equal(SemanticAnchorConflictReason.SubtreeHashMismatch, error.Reason);
        Assert.Equal(latest, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceOwnText_AcceptsLegacySelfHashAnchor()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, "- Parent\n  - Child\n",
            TestContext.Current.CancellationToken);
        var parent = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        var anchor = new SemanticAnchor(parent.Pointer, parent.SelfHash).ToString();
        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                anchor, "- New parent")]), TestContext.Current.CancellationToken);
        Assert.Equal("- New parent\n  - Child\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InsertAfterParent_GoesAfterDescendants()
    {
        const string source = "- Parent\n  - Child\n- End\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var element = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(
                PatchOperationKind.InsertAfter,
                SemanticAnchor.FromElement(element).ToString(), "- New")]),
            TestContext.Current.CancellationToken);
        Assert.Equal("- Parent\n  - Child\n- New\n- End\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaleSubtree_PreventsDestructiveMutation()
    {
        const string source = "- Parent\n  - Changed\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, "- Parent\n  - Old\n",
            TestContext.Current.CancellationToken);
        var old = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<SemanticAnchorConflictException>(() =>
            CreateService(temp.Path).PatchAsync(
                new PatchRequest("test.md", [new PatchOperation(
                    PatchOperationKind.Delete, SemanticAnchor.FromElement(old).ToString())]),
                TestContext.Current.CancellationToken));
        Assert.Equal(source, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WholeQuoteReplacement_DoesNotAddressInnerLists()
    {
        const string source = "> - Old\n> # Header\n\nAfter.\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var elements = Read(temp.Path);
        Assert.DoesNotContain(elements, x => x.Kind == MarkdownElementKind.ListItem);
        var quote = elements.Single(x => x.Kind == MarkdownElementKind.BlockQuote);
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(
                PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(quote).ToString(), "> New quote")]),
            TestContext.Current.CancellationToken);
        Assert.Equal("> New quote\n\nAfter.\n",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task MissingListSubtreeHash_IsRejectedAtomically()
    {
        const string source = "- Parent\n  - Child\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var item = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        var legacy = new SemanticAnchor(item.Pointer, item.SelfHash).ToString();
        var error = await Assert.ThrowsAsync<SemanticAnchorConflictException>(() =>
            CreateService(temp.Path).PatchAsync(
                new PatchRequest("test.md", [new PatchOperation(PatchOperationKind.Delete, legacy)]),
                TestContext.Current.CancellationToken));
        Assert.Equal(SemanticAnchorConflictReason.MissingSubtreeHash, error.Reason);
        Assert.Equal(source, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InsertAfterParent_DoesNotRequireCurrentChildSubtreeHash()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, "- Parent\n  - Child\n",
            TestContext.Current.CancellationToken);
        var parent = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await File.WriteAllTextAsync(file, "- Parent\n  - Changed child\n",
            TestContext.Current.CancellationToken);
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(
                PatchOperationKind.InsertAfter, SemanticAnchor.FromElement(parent).ToString(),
                "- Sibling")]), TestContext.Current.CancellationToken);
        Assert.Equal("- Parent\n  - Changed child\n- Sibling",
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MultipleSiblingRootsInFragment_AreRejectedWithoutWrite()
    {
        const string source = "- A\n- B\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var first = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            CreateService(temp.Path).PatchAsync(
                new PatchRequest("test.md", [new PatchOperation(
                    PatchOperationKind.ReplaceElement,
                    SemanticAnchor.FromElement(first).ToString(), "- X\n- Y")]),
                TestContext.Current.CancellationToken));
        Assert.Equal(source, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OldParagraphAnchorInList_IsReservedRatherThanRelocated()
    {
        const string source = "Same\n\n- Same\n\nSame\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var oldAnchor = new SemanticAnchor(new SemanticPointer("p2"),
            SemanticFingerprint.Compute("Same")).ToString();
        await Assert.ThrowsAsync<SemanticAnchorConflictException>(() =>
            CreateService(temp.Path).PatchAsync(
                new PatchRequest("test.md", [new PatchOperation(
                    PatchOperationKind.Delete, oldAnchor)]),
                TestContext.Current.CancellationToken));
        Assert.Equal(source, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }
    private static IReadOnlyList<MarkdownElement> Read(string root)
        => new MarkdownElementParser().Parse(
            new MarkdownSourceDocument("test.md", Path.Combine(root, "test.md"),
                File.ReadAllText(Path.Combine(root, "test.md")), "",
                DateTimeOffset.UtcNow));

    private static WorkspaceMutationService CreateService(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = root, AllowWrites = true }
        };
        return new WorkspaceMutationService(config, new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(), new MarkdownElementParser(),
            new ImmediateIndexSynchronizationScheduler(new NoOpSynchronizer()));
    }

    private sealed class NoOpSynchronizer : IWorkspaceIndexSynchronizer
    {
        public Task<bool> ReconcileAsync(string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
