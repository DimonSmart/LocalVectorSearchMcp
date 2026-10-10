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
    public async Task Replace_ExchangesCompleteNestedSubtree()
    {
        const string source = "- Vegetables\n  - Onion\n  - Tomato\n- Meat\n";
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(file, source, TestContext.Current.CancellationToken);
        var element = Read(temp.Path).Single(x => x.Pointer.Value == "li1");
        await CreateService(temp.Path).PatchAsync(
            new PatchRequest("test.md", [new PatchOperation(
                PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(element).ToString(),
                "- Fruits\n  - Apple\n  - Pear")]),
            TestContext.Current.CancellationToken);
        Assert.Equal("- Fruits\n  - Apple\n  - Pear\n- Meat\n",
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
