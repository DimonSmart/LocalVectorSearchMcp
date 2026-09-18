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

public sealed class SemanticPatchIntegrationTests
{
    [Fact]
    public async Task Patch_RequiresFingerprintForConcreteElement()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        const string source = "# One\n\nTarget.\n";
        await File.WriteAllTextAsync(
            path,
            source,
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(PatchOperationKind.Delete, "1.p1")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "A fingerprint is required when mutating a concrete semantic element.",
            exception.Message);
        Assert.Equal(
            source,
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_AllowsDocumentInsertionWithoutFingerprint()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        await File.WriteAllTextAsync(
            path,
            "",
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.InsertAfter,
                    "document",
                    "# Chapter")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "# Chapter",
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_PreservesUnrelatedHumanEdit()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        await File.WriteAllTextAsync(
            path,
            "# One\n\nTarget.\n\nOther.\n",
            TestContext.Current.CancellationToken);
        var anchor = Anchor("1.p1", "Target.");
        await File.WriteAllTextAsync(
            path,
            "# One\n\nTarget.\n\nHuman edit.\n",
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.Replace,
                    anchor,
                    "Agent edit.")]),
            TestContext.Current.CancellationToken);

        var result = await File.ReadAllTextAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.Contains("Agent edit.", result);
        Assert.Contains("Human edit.", result);
    }

    [Fact]
    public async Task Patch_RelocatesTargetAfterStructuralShift()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        var anchor = Anchor("1.p1", "Target.");
        await File.WriteAllTextAsync(
            path,
            "# One\n\nInserted.\n\nTarget.\n",
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.Replace,
                    anchor,
                    "Changed.")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "# One\n\nInserted.\n\nChanged.\n",
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_RejectsChangedTarget()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        var anchor = Anchor("1.p1", "Original.");
        const string current = "# One\n\nHuman changed target.\n";
        await File.WriteAllTextAsync(
            path,
            current,
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.Replace,
                        anchor,
                        "Agent edit.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            current,
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_RejectsDeletedTarget()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        var anchor = Anchor("1.p1", "Deleted.");
        const string current = "# One\n";
        await File.WriteAllTextAsync(
            path,
            current,
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(PatchOperationKind.Delete, anchor)]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "The original element could not be found in the current document.",
            exception.Message);
        Assert.Equal(
            current,
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_RejectsAmbiguousRelocation()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        var anchor = Anchor("1.p1", "TODO");
        const string current = "# One\n\nChanged.\n\nTODO\n\nTODO\n";
        await File.WriteAllTextAsync(
            path,
            current,
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        var exception = await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [new PatchOperation(
                        PatchOperationKind.Replace,
                        anchor,
                        "Done.")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "The element fingerprint matches multiple current elements and cannot be relocated safely.",
            exception.Message);
        Assert.Equal(
            current,
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_DirectMatchWinsWhenIdenticalElementExistsElsewhere()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        var anchor = Anchor("1.p1", "TODO");
        await File.WriteAllTextAsync(
            path,
            "# One\n\nTODO\n\nTODO\n",
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [new PatchOperation(
                    PatchOperationKind.Replace,
                    anchor,
                    "Done.")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "# One\n\nDone.\n\nTODO\n",
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_ResolvesAllOperationsAgainstSameRevision()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        await File.WriteAllTextAsync(
            path,
            "# One\n\nA.\n\nB.\n",
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await service.PatchAsync(
            new PatchRequest(
                "chapter.md",
                [
                    new PatchOperation(
                        PatchOperationKind.Replace,
                        Anchor("1.p1", "A."),
                        "AA."),
                    new PatchOperation(
                        PatchOperationKind.Delete,
                        Anchor("1.p2", "B."))
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "# One\n\nAA.\n\n\n",
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_RejectsTwoAnchorsRelocatedToSameTargetAtomically()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        const string current = "# One\n\nChanged.\n\nTODO\n";
        await File.WriteAllTextAsync(
            path,
            current,
            TestContext.Current.CancellationToken);
        var fingerprint = SemanticFingerprint.Compute("TODO");
        var service = CreateService(temp.Path);

        await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [
                        new PatchOperation(
                            PatchOperationKind.Replace,
                            $"1.p1~{fingerprint}",
                            "First."),
                        new PatchOperation(
                            PatchOperationKind.Delete,
                            $"1.p3~{fingerprint}")
                    ]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            current,
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Patch_OneInvalidOperationLeavesFileUnchanged()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "chapter.md");
        const string current = "# One\n\nA.\n\nB.\n";
        await File.WriteAllTextAsync(
            path,
            current,
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);

        await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.PatchAsync(
                new PatchRequest(
                    "chapter.md",
                    [
                        new PatchOperation(
                            PatchOperationKind.Replace,
                            Anchor("1.p1", "A."),
                            "AA."),
                        new PatchOperation(
                            PatchOperationKind.Delete,
                            Anchor("1.p2", "Old B."))
                    ]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            current,
            await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken));
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
            new NoOpSynchronizer());
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
