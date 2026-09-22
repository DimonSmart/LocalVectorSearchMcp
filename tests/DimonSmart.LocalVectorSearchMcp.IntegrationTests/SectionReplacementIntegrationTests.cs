using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class SectionReplacementIntegrationTests
{
    [Theory]
    [InlineData(PatchOperationKind.Replace)]
    [InlineData(PatchOperationKind.ReplaceElement)]
    public async Task ElementReplacement_HeadingChangesOnlyHeading(PatchOperationKind kind)
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nParagraph A1.\n\nParagraph A2.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(kind, Anchor("1", "### Section A"), "### Renamed Section")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "### Renamed Section\n\nParagraph A1.\n\nParagraph A2.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PatchOperationKind.Replace)]
    [InlineData(PatchOperationKind.ReplaceElement)]
    public async Task ElementReplacement_RejectsMultipleElementsAtomically(PatchOperationKind kind)
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld text.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        kind,
                        Anchor("1", "### Section A"),
                        "### Section A\n\nNew paragraph.")]),
                TestContext.Current.CancellationToken));

        Assert.Contains("exactly one editable Markdown element", exception.Message, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceElement_RejectsHeadingLevelChange()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld text.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceElement,
                        Anchor("1", "### Section A"),
                        "## Section A")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_ReplacesNestedSectionAndPreservesSibling()
    {
        using var temp = new TemporaryDirectory();
        const string source =
            "## Chapter 1\n\n" +
            "### Section A\n\n" +
            "Paragraph A1.\n\n" +
            "<!-- non-indexed content -->\n\n" +
            "#### Details\n\n" +
            "Details text.\n\n" +
            "Paragraph A2.\n\n" +
            "### Section B\n\n" +
            "Paragraph B1.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    Anchor("1.1", "### Section A"),
                    "### Section A\n\nNew paragraph.")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "## Chapter 1\n\n### Section A\n\nNew paragraph.\n\n### Section B\n\nParagraph B1.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_AtEofConsumesRemainingSource()
    {
        using var temp = new TemporaryDirectory();
        var path = await WriteAsync(
            temp.Path,
            "### Section A\n\nOld.\n\n#### Child\n\nChild text.\n");
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    Anchor("1", "### Section A"),
                    "### Section A\n\nNew.")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "### Section A\n\nNew.",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_RequiresHeadingPointer()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld text.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        Anchor("1.p1", "Old text."),
                        "### Section A\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal("replace_section requires a heading pointer.", exception.Message);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("## Section A\n\nText.")]
    [InlineData("### Section A\n\nText.\n\n### Another section\n\nText.")]
    public async Task ReplaceSection_RejectsInvalidRootHierarchy(string replacement)
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        Anchor("1", "### Section A"),
                        replacement)]),
                TestContext.Current.CancellationToken));

        Assert.Contains("exactly one root section", exception.Message, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_AllowsNestedReplacementHeadings()
    {
        using var temp = new TemporaryDirectory();
        var path = await WriteAsync(
            temp.Path,
            "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n");
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    Anchor("1", "### Section A"),
                    "### Section A\n\nText.\n\n#### Child\n\nChild text.\n\n##### Details\n\nDetails text.")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "### Section A\n\nText.\n\n#### Child\n\nChild text.\n\n##### Details\n\nDetails text.\n\n### Section B\n\nKeep.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_SafelyRelocatesShiftedHeading()
    {
        using var temp = new TemporaryDirectory();
        var path = await WriteAsync(
            temp.Path,
            "# First\n\nA.\n\n# Inserted\n\nX.\n\n# Target\n\nOld.\n");
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    Anchor("2", "# Target"),
                    "# Target\n\nNew.")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "# First\n\nA.\n\n# Inserted\n\nX.\n\n# Target\n\nNew.",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_RejectsAmbiguousRelocation()
    {
        using var temp = new TemporaryDirectory();
        const string source =
            "# First\n\nA.\n\n" +
            "# Changed\n\nB.\n\n" +
            "# Target\n\nC.\n\n" +
            "# Target\n\nD.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        Anchor("2", "# Target"),
                        "# Target\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_OverlappingInnerOperationRejectsWholePatch()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nParagraph A1.\n\n### Section B\n\nKeep.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [
                        new PatchOperation(
                            PatchOperationKind.ReplaceSection,
                            Anchor("1", "### Section A"),
                            "### Section A\n\nNew."),
                        new PatchOperation(
                            PatchOperationKind.ReplaceElement,
                            Anchor("1.p1", "Paragraph A1."),
                            "Changed.")
                    ]),
                TestContext.Current.CancellationToken));

        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_BoundarySiblingCanBeEditedInSamePatch()
    {
        using var temp = new TemporaryDirectory();
        var path = await WriteAsync(
            temp.Path,
            "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n");
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [
                    new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        Anchor("1", "### Section A"),
                        "### Section A\n\nNew."),
                    new PatchOperation(
                        PatchOperationKind.ReplaceElement,
                        Anchor("2", "### Section B"),
                        "### Renamed B")
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "### Section A\n\nNew.\n\n### Renamed B\n\nKeep.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_PreservesCrLfAndUtf8Bom()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\r\n\r\nOld.\r\n\r\n### Section B\r\n\r\nKeep.\r\n";
        var path = Path.Combine(temp.Path, "chapter.md");
        var bytes = new UTF8Encoding(true).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(source))
            .ToArray();
        await File.WriteAllBytesAsync(
            path,
            bytes,
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    Anchor("1", "### Section A"),
                    "### Section A\n\nNew.")]),
            TestContext.Current.CancellationToken);

        var resultBytes = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.True(resultBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var result = new UTF8Encoding(true).GetString(resultBytes).TrimStart('\uFEFF');
        Assert.Equal(
            "### Section A\r\n\r\nNew.\r\n\r\n### Section B\r\n\r\nKeep.\r\n",
            result);
        Assert.DoesNotContain("\n", result.Replace("\r\n", "", StringComparison.Ordinal));
    }

    private static async Task<string> WriteAsync(string root, string source)
    {
        var path = Path.Combine(root, "chapter.md");
        await File.WriteAllTextAsync(
            path,
            source,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        return path;
    }

    private static WorkspaceMutationService CreateService(string root)
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

    private static string Anchor(string pointer, string exactText)
        => new SemanticAnchor(
            new SemanticPointer(pointer),
            SemanticFingerprint.Compute(exactText)).ToString();

    private sealed class NoOpSynchronizer : IWorkspaceIndexSynchronizer
    {
        public Task<bool> ReconcileAsync(
            string relativePath,
            CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
