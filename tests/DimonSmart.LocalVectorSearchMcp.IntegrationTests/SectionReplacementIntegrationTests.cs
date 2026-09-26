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
                [new PatchOperation(kind, LegacyAnchor("1", "### Section A"), "### Renamed Section")]),
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
                        LegacyAnchor("1", "### Section A"),
                        "### Section A\n\nNew paragraph.")]),
                TestContext.Current.CancellationToken));

        Assert.Contains("exactly one editable Markdown element", exception.Message, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SelfMutation_V2HeadingPointerSurvivesDescendantEdit()
    {
        using var temp = new TemporaryDirectory();
        const string original = "### Section A\n\nOld text.\n";
        const string current = "### Section A\n\nHuman changed body.\n";
        var path = await WriteAsync(temp.Path, current);
        var pointer = V2Anchor(original, "1");
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceElement,
                    pointer,
                    "### Renamed Section")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "### Renamed Section\n\nHuman changed body.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
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
                        LegacyAnchor("1", "### Section A"),
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
                    V2Anchor(source, "1.1"),
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
        const string source = "### Section A\n\nOld.\n\n#### Child\n\nChild text.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    V2Anchor(source, "1"),
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
                        V2Anchor(source, "1.p1"),
                        "### Section A\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal("replace_section requires a heading pointer.", exception.Message);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
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
                        V2Anchor(source, "1"),
                        replacement)]),
                TestContext.Current.CancellationToken));

        Assert.Contains("exactly one root section", exception.Message, StringComparison.Ordinal);
        Assert.Contains("delete_section", exception.Message, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_AllowsNestedReplacementHeadings()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    V2Anchor(source, "1"),
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
        const string original = "# First\n\nA.\n\n# Target\n\nOld.\n";
        const string current = "# First\n\nA.\n\n# Inserted\n\nX.\n\n# Target\n\nOld.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.ReplaceSection,
                    V2Anchor(original, "2"),
                    "# Target\n\nNew.")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "# First\n\nA.\n\n# Inserted\n\nX.\n\n# Target\n\nNew.",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_RelocationThenRejectsChangedSubtree()
    {
        using var temp = new TemporaryDirectory();
        const string original = "# First\n\nA.\n\n# Target\n\nOld.\n";
        const string current = "# First\n\nA.\n\n# Inserted\n\nX.\n\n# Target\n\nHuman edit.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        V2Anchor(original, "2"),
                        "# Target\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.SubtreeHashMismatch, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_RejectsDescendantEdit()
    {
        using var temp = new TemporaryDirectory();
        const string original = "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n";
        const string current = "### Section A\n\nHuman edit.\n\n### Section B\n\nKeep.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        V2Anchor(original, "1"),
                        "### Section A\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.SubtreeHashMismatch, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_RejectsRawNonElementEdit()
    {
        using var temp = new TemporaryDirectory();
        const string original =
            "### Section A\n\nText.\n\n<!-- original comment -->\n\n### Section B\n";
        const string current =
            "### Section A\n\nText.\n\n<!-- human comment edit -->\n\n### Section B\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        V2Anchor(original, "1"),
                        "### Section A\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.SubtreeHashMismatch, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_LegacyAnchorIsRejectedBeforeMutation()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        LegacyAnchor("1", "### Section A"),
                        "### Section A\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.MissingSubtreeHash, exception.Reason);
        Assert.Contains("subtree hash", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_RejectsAmbiguousRelocation()
    {
        using var temp = new TemporaryDirectory();
        const string original = "# First\n\nA.\n\n# Target\n\nC.\n";
        const string current =
            "# First\n\nA.\n\n" +
            "# Changed\n\nB.\n\n" +
            "# Target\n\nC.\n\n" +
            "# Target\n\nD.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        V2Anchor(original, "2"),
                        "# Target\n\nNew.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.AmbiguousSemanticPointer, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
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
                            V2Anchor(source, "1"),
                            "### Section A\n\nNew."),
                        new PatchOperation(
                            PatchOperationKind.ReplaceElement,
                            V2Anchor(source, "1.p1"),
                            "Changed.")
                    ]),
                TestContext.Current.CancellationToken));

        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BatchWithValidSelfAndStaleSubtreeRejectsEverything()
    {
        using var temp = new TemporaryDirectory();
        const string original =
            "### Section A\n\nOld A.\n\n### Section B\n\nStable B.\n";
        const string current =
            "### Section A\n\nHuman A.\n\n### Section B\n\nStable B.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [
                        new PatchOperation(
                            PatchOperationKind.ReplaceElement,
                            V2Anchor(original, "2.p1"),
                            "Agent B."),
                        new PatchOperation(
                            PatchOperationKind.ReplaceSection,
                            V2Anchor(original, "1"),
                            "### Section A\n\nAgent A.")
                    ]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.SubtreeHashMismatch, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSection_BoundarySiblingCanBeEditedInSamePatch()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [
                    new PatchOperation(
                        PatchOperationKind.ReplaceSection,
                        V2Anchor(source, "1"),
                        "### Section A\n\nNew."),
                    new PatchOperation(
                        PatchOperationKind.ReplaceElement,
                        V2Anchor(source, "2"),
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
                    V2Anchor(source, "1"),
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

    [Fact]
    public async Task DeleteSection_RemovesWholeTopLevelSection()
    {
        using var temp = new TemporaryDirectory();
        const string source =
            "## Chapter 1\n\nText 1.\n\n### Child\n\nChild text.\n\n" +
            "## Chapter 2\n\nText 2.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.DeleteSection,
                    V2Anchor(source, "1"))]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "## Chapter 2\n\nText 2.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_RemovesNestedSectionAndPreservesSibling()
    {
        using var temp = new TemporaryDirectory();
        const string source =
            "## Chapter\n\n" +
            "### Section A\n\nA.\n\n" +
            "#### Details\n\nDetails.\n\n" +
            "### Section B\n\nB.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.DeleteSection,
                    V2Anchor(source, "1.1"))]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "## Chapter\n\n### Section B\n\nB.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_AtEofConsumesRemainingSource()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld.\n\n#### Child\n\nChild text.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.DeleteSection,
                    V2Anchor(source, "1"))]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_RequiresHeadingPointer()
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
                        PatchOperationKind.DeleteSection,
                        V2Anchor(source, "1.p1"))]),
                TestContext.Current.CancellationToken));

        Assert.Equal("delete_section requires a heading pointer.", exception.Message);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_RejectsMarkdownContent()
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
                        PatchOperationKind.DeleteSection,
                        V2Anchor(source, "1"),
                        "ignored")]),
                TestContext.Current.CancellationToken));

        Assert.Equal("delete_section does not accept markdown content.", exception.Message);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_LegacyAnchorIsRejectedBeforeMutation()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.DeleteSection,
                        LegacyAnchor("1", "### Section A"))]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.MissingSubtreeHash, exception.Reason);
        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_RejectsStaleSubtree()
    {
        using var temp = new TemporaryDirectory();
        const string original = "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n";
        const string current = "### Section A\n\nHuman edit.\n\n### Section B\n\nKeep.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.DeleteSection,
                        V2Anchor(original, "1"))]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.SubtreeHashMismatch, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_SafelyRelocatesShiftedHeading()
    {
        using var temp = new TemporaryDirectory();
        const string original = "# First\n\nA.\n\n# Target\n\nOld.\n";
        const string current = "# First\n\nA.\n\n# Inserted\n\nX.\n\n# Target\n\nOld.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.DeleteSection,
                    V2Anchor(original, "2"))]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "# First\n\nA.\n\n# Inserted\n\nX.\n\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_RejectsAmbiguousRelocation()
    {
        using var temp = new TemporaryDirectory();
        const string original = "# First\n\nA.\n\n# Target\n\nC.\n";
        const string current =
            "# First\n\nA.\n\n" +
            "# Changed\n\nB.\n\n" +
            "# Target\n\nC.\n\n" +
            "# Target\n\nD.\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.DeleteSection,
                        V2Anchor(original, "2"))]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.AmbiguousSemanticPointer, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_RejectsRawNonElementEdit()
    {
        using var temp = new TemporaryDirectory();
        const string original =
            "### Section A\n\nText.\n\n<!-- original comment -->\n\n### Section B\n";
        const string current =
            "### Section A\n\nText.\n\n<!-- human comment edit -->\n\n### Section B\n";
        var path = await WriteAsync(temp.Path, current);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.DeleteSection,
                        V2Anchor(original, "1"))]),
                TestContext.Current.CancellationToken));

        Assert.Equal(SemanticAnchorConflictReason.SubtreeHashMismatch, exception.Reason);
        Assert.Equal(current, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_OverlappingInnerOperationRejectsWholePatch()
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
                            PatchOperationKind.DeleteSection,
                            V2Anchor(source, "1")),
                        new PatchOperation(
                            PatchOperationKind.ReplaceElement,
                            V2Anchor(source, "1.p1"),
                            "Changed.")
                    ]),
                TestContext.Current.CancellationToken));

        Assert.Equal(source, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_BoundarySiblingCanBeEditedInSamePatch()
    {
        using var temp = new TemporaryDirectory();
        const string source = "### Section A\n\nOld.\n\n### Section B\n\nKeep.\n";
        var path = await WriteAsync(temp.Path, source);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [
                    new PatchOperation(
                        PatchOperationKind.DeleteSection,
                        V2Anchor(source, "1")),
                    new PatchOperation(
                        PatchOperationKind.ReplaceElement,
                        V2Anchor(source, "2"),
                        "### Renamed B")
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "### Renamed B\n\nKeep.\n",
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSection_PreservesCrLfAndUtf8Bom()
    {
        using var temp = new TemporaryDirectory();
        const string source =
            "### Section A\r\n\r\nOld.\r\n\r\n### Section B\r\n\r\nKeep.\r\n";
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
                    PatchOperationKind.DeleteSection,
                    V2Anchor(source, "1"))]),
            TestContext.Current.CancellationToken);

        var resultBytes = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.True(resultBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var result = new UTF8Encoding(true).GetString(resultBytes).TrimStart('\uFEFF');
        Assert.Equal(
            "### Section B\r\n\r\nKeep.\r\n",
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
            new ImmediateIndexSynchronizationScheduler(new NoOpSynchronizer()));
    }

    private static string LegacyAnchor(string pointer, string exactText)
        => new SemanticAnchor(
            new SemanticPointer(pointer),
            SemanticFingerprint.Compute(exactText)).ToString();

    private static string V2Anchor(string source, string pointer)
    {
        var document = new MarkdownSourceDocument(
            "chapter.md",
            "chapter.md",
            source,
            "hash",
            DateTimeOffset.UtcNow);
        var element = new MarkdownElementParser().Parse(document)
            .Single(candidate => candidate.Pointer.Value == pointer);
        return SemanticAnchor.FromElement(element).ToString();
    }

    private sealed class NoOpSynchronizer : IWorkspaceIndexSynchronizer
    {
        public Task<bool> ReconcileAsync(
            string relativePath,
            CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
