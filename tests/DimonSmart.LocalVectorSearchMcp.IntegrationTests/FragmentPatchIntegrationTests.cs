using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class FragmentPatchIntegrationTests
{
    [Fact]
    public async Task ReplacesOnlyOneInlineImageAndPreservesOtherText()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        const string paragraph = "Text ![Old](a.png) and ![Keep](b.png).";
        await File.WriteAllTextAsync(path, "# Title\n\n" + paragraph + "\n");
        await Service(temp.Path).PatchAsync(new PatchRequest("chapter.md", [
            Fragment("1.p1", paragraph, "![Old](a.png)", "![New](../assets/a.png)")
        ]), TestContext.Current.CancellationToken);
        Assert.Equal("# Title\n\nText ![New](../assets/a.png) and ![Keep](b.png).\n",
            await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task TwoDisjointFragmentsOfSameAnchorUseOriginalRevision()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        const string paragraph = "alpha ![A](a.png) middle ![B](b.png) omega";
        await File.WriteAllTextAsync(path, paragraph + "\n");
        await Service(temp.Path).PatchAsync(new PatchRequest("chapter.md", [
            Fragment("p1", paragraph, "![A](a.png)", "![A](assets/a.png)"),
            Fragment("p1", paragraph, "![B](b.png)", "![B](assets/b.png)")
        ]), TestContext.Current.CancellationToken);
        Assert.Equal("alpha ![A](assets/a.png) middle ![B](assets/b.png) omega\n",
            await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("not there", "fragment_not_found")]
    [InlineData("aaa", "ambiguous_fragment")]
    public async Task MissingAndOverlappingOccurrencesAreRejected(string old, string code)
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        await File.WriteAllTextAsync(path, "aaaaa\n");
        var error = await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            Service(temp.Path).PatchAsync(new PatchRequest("chapter.md", [
                Fragment("p1", "aaaaa", old, "B")
            ]), TestContext.Current.CancellationToken));
        Assert.Contains(code, error.Message);
        Assert.Equal("aaaaa\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task BatchConflictNeverCommitsPartialChanges()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        const string paragraph = "abcdefghij";
        await File.WriteAllTextAsync(path, paragraph + "\n");
        await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            Service(temp.Path).PatchAsync(new PatchRequest("chapter.md", [
                Fragment("p1", paragraph, "abcde", "X"),
                Fragment("p1", paragraph, "cdef", "Y")
            ]), TestContext.Current.CancellationToken));
        Assert.Equal(paragraph + "\n", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("alpha\n\nbeta")]
    [InlineData("# Heading")]
    public async Task ParagraphCannotChangeBlockStructure(string replacement)
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        await File.WriteAllTextAsync(path, "hello world\n");
        await Assert.ThrowsAsync<WorkspaceMutationException>(() =>
            Service(temp.Path).PatchAsync(new PatchRequest("chapter.md", [
                Fragment("p1", "hello world", "hello world", replacement)
            ]), TestContext.Current.CancellationToken));
        Assert.Equal("hello world\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task DocumentInsertionPreservesYamlFrontMatter()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        await File.WriteAllTextAsync(path, "---\ntitle: Test\n---\n\nOriginal.\n");
        await Service(temp.Path).PatchAsync(new PatchRequest("chapter.md", [
            new PatchOperation(PatchOperationKind.InsertBefore, "document", "Inserted.")
        ]), TestContext.Current.CancellationToken);
        var updated = await File.ReadAllTextAsync(path);
        Assert.StartsWith("---\ntitle: Test\n---\n", updated);
        Assert.True(updated.IndexOf("Inserted.", StringComparison.Ordinal) <
                    updated.IndexOf("Original.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BomAndCrLfAreUnchangedOutsideFragment()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        const string paragraph = "Some ![old](a.png) text";
        var original = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes("# H\r\n\r\n" + paragraph + "\r\n"))
            .ToArray();
        await File.WriteAllBytesAsync(path, original);
        await Service(temp.Path).PatchAsync(new PatchRequest("chapter.md", [
            Fragment("1.p1", paragraph, "![old](a.png)", "![new](b.png)")
        ]), TestContext.Current.CancellationToken);
        var updated = await File.ReadAllBytesAsync(path);
        Assert.True(updated.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble));
        Assert.Equal("# H\r\n\r\nSome ![new](b.png) text\r\n",
            System.Text.Encoding.UTF8.GetString(updated.AsSpan(3)));
    }

    private static PatchOperation Fragment(string pointer, string original,
        string old, string replacement)
        => new(PatchOperationKind.ReplaceFragment,
            new SemanticAnchor(new SemanticPointer(pointer), SemanticFingerprint.Compute(original)).ToString(),
            replacement, old);

    private static WorkspaceMutationService Service(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = root, AllowWrites = true }
        };
        return new WorkspaceMutationService(config, new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(), new MarkdownElementParser(), new RecordingScheduler());
    }

    private sealed class RecordingScheduler : IWorkspaceIndexSynchronizationScheduler
    {
        public void Schedule(string relativePath) { }
    }
}
