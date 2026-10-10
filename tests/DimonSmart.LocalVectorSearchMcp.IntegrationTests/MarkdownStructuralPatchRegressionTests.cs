using System.Text;
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

public sealed class MarkdownStructuralPatchRegressionTests
{
    [Theory]
    [InlineData("- Replacement\n\n<!-- detached -->")]
    [InlineData("- First\n- Second")]
    [InlineData("- Replacement\n\n---")]
    [InlineData("- Replacement\n\n[ref]: https://example.org")]
    public async Task ReplaceListItem_WithExtraRootBlock_LeavesFileByteIdentical(
        string replacement)
    {
        await AssertRejectedWithoutWrite("- A\n- B\n", "li1",
            PatchOperationKind.ReplaceElement, replacement);
    }

    [Fact]
    public async Task InsertListItem_WithDetachedComment_LeavesFileByteIdentical()
    {
        await AssertRejectedWithoutWrite("- A\n- B\n", "li1",
            PatchOperationKind.InsertAfter,
            "- New\n\n<!-- detached -->");
    }

    [Fact]
    public async Task ReplaceQuote_WithDetachedBlock_LeavesFileByteIdentical()
    {
        await AssertRejectedWithoutWrite("> Original\n\nOutside.\n", "q1",
            PatchOperationKind.ReplaceElement,
            "> Updated\n\n<!-- detached -->");
    }

    [Fact]
    public async Task ReplaceItem_PreservesCommentProperlyNestedInNewItem()
    {
        const string source = "- A\n- B\n";
        const string replacement = "- Changed\n\n  <!-- nested -->";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
        var target = Elements(source).Single(x => x.Pointer.Value == "li1");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(target).ToString(), replacement)]),
            TestContext.Current.CancellationToken);

        Assert.Equal("- Changed\n\n  <!-- nested -->\n- B\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceItem_AllowsIndentedFencedCodeInsideSubtree()
    {
        const string source = "- A\n- B\n";
        const string replacement = "- Changed\n\n  ```text\n  inside\n  ```";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
        var target = Elements(source).Single(x => x.Pointer.Value == "li1");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(target).ToString(), replacement)]),
            TestContext.Current.CancellationToken);

        Assert.Equal("- Changed\n\n  ```text\n  inside\n  ```\n- B\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MultipleNonOverlappingEdits_AreCheckedAsOneRevision()
    {
        const string source = "- A\n- B\n";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
        var elements = Elements(source);
        var first = elements.Single(x => x.Pointer.Value == "li1");
        var second = elements.Single(x => x.Pointer.Value == "li2");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [
                new PatchOperation(PatchOperationKind.InsertBefore,
                    SemanticAnchor.FromElement(first).ToString(), "- New"),
                new PatchOperation(PatchOperationKind.ReplaceElement,
                    SemanticAnchor.FromElement(second).ToString(), "- Updated")
            ]), TestContext.Current.CancellationToken);

        Assert.Equal("- New\n- A\n- Updated\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OverlappingParentAndChildEdits_LeaveFileByteIdentical()
    {
        const string source = "- Parent\n  - Child\n- Other\n";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        var original = new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(source)).ToArray();
        await File.WriteAllBytesAsync(path, original, TestContext.Current.CancellationToken);
        var items = Elements(source).Where(x => x.Kind == MarkdownElementKind.ListItem)
            .ToArray();

        await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
                [
                    new PatchOperation(PatchOperationKind.Delete,
                        SemanticAnchor.FromElement(items[0]).ToString()),
                    new PatchOperation(PatchOperationKind.Delete,
                        SemanticAnchor.FromElement(items[1]).ToString())
                ]), TestContext.Current.CancellationToken));
        Assert.Equal(original,
            await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    private static async Task AssertRejectedWithoutWrite(
        string source, string pointer, PatchOperationKind kind, string replacement)
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        var original = new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(source)).ToArray();
        await File.WriteAllBytesAsync(path, original, TestContext.Current.CancellationToken);
        var target = Elements(source).Single(x => x.Pointer.Value == pointer);

        await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            CreateService(temp.Path).PatchAsync(
                new PatchRequest("test.md", [new PatchOperation(kind,
                    SemanticAnchor.FromElement(target).ToString(), replacement)]),
                TestContext.Current.CancellationToken));

        Assert.Equal(original,
            await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    private static IReadOnlyList<MarkdownElement> Elements(string source)
        => new MarkdownElementParser().Parse(new MarkdownSourceDocument(
            "test.md", "test.md", source, "", DateTimeOffset.UtcNow));

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
