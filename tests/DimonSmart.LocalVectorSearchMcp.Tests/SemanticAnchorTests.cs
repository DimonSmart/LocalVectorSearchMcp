using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class SemanticAnchorTests
{
    [Fact]
    public void Fingerprint_IsCanonicalStableAndExact()
    {
        var fingerprint = SemanticFingerprint.Compute("Text.  \r\n");

        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
        Assert.Equal(fingerprint, SemanticFingerprint.Compute("Text.  \r\n"));
        Assert.NotEqual(fingerprint, SemanticFingerprint.Compute("Text.\r\n"));
        Assert.NotEqual(
            SemanticFingerprint.Compute("line\nnext"),
            SemanticFingerprint.Compute("line\r\nnext"));
        Assert.NotEqual(
            SemanticFingerprint.Compute("Text."),
            SemanticFingerprint.Compute("Text. "));
    }

    [Fact]
    public void AnchorParser_AcceptsLogicalLegacyAndV2()
    {
        var logical = SemanticAnchorParser.Parse("1.2.p3");
        var legacy = SemanticAnchorParser.Parse("1.2.p3~ABCDEF0123456789");
        var v2 = SemanticAnchorParser.Parse(
            "1.2.p3~ABCDEF0123456789~FEDCBA9876543210");

        Assert.Null(logical.SelfHash);
        Assert.Null(logical.SubtreeHash);
        Assert.Equal("1.2.p3~abcdef0123456789", legacy.ToString());
        Assert.True(legacy.IsLegacy);
        Assert.Equal(
            "1.2.p3~abcdef0123456789~fedcba9876543210",
            v2.ToString());
        Assert.False(v2.IsLegacy);
    }

    [Theory]
    [InlineData("1.p1~xyz")]
    [InlineData("1.p1~1234")]
    [InlineData("1.p1~0123456789abcdef~0")]
    [InlineData("1.p1~0123456789abcdef~fedcba9876543210~0")]
    [InlineData("document~0123456789abcdef")]
    [InlineData("document~0123456789abcdef~fedcba9876543210")]
    public void AnchorParser_RejectsMalformedHashes(string value)
    {
        var exception = Assert.Throws<SemanticPointerFormatException>(
            () => SemanticAnchorParser.Parse(value));

        Assert.Equal("Invalid semantic pointer fingerprint.", exception.Message);
    }

    [Fact]
    public void Parser_ComputesHeadingSelfAndOwnedSubtreeHashes()
    {
        const string source =
            "## Chapter\n\nIntro.\n\n" +
            "### Section A\n\nText A.\n\n" +
            "#### Detail\n\nDetail text.\n\n" +
            "### Section B\n\nText B.\n";
        var elements = Parse(source);
        var chapter = elements.Single(element => element.Pointer.Value == "1");
        var sectionA = elements.Single(element => element.Pointer.Value == "1.1");
        var detail = elements.Single(element => element.Pointer.Value == "1.1.1");
        var sectionB = elements.Single(element => element.Pointer.Value == "1.2");

        Assert.Equal(
            SemanticFingerprint.Compute("## Chapter"),
            chapter.SelfHash);
        Assert.Equal(
            SemanticFingerprint.Compute(source.AsSpan(
                chapter.SourceStart,
                source.Length - chapter.SourceStart)),
            chapter.SubtreeHash);
        Assert.Equal(
            SemanticFingerprint.Compute(source.AsSpan(
                sectionA.SourceStart,
                sectionB.SourceStart - sectionA.SourceStart)),
            sectionA.SubtreeHash);
        Assert.Equal(
            SemanticFingerprint.Compute(source.AsSpan(
                detail.SourceStart,
                sectionB.SourceStart - detail.SourceStart)),
            detail.SubtreeHash);
    }

    [Fact]
    public void DescendantEdit_ChangesOwningSubtreesButNotHeadingSelfHashes()
    {
        const string before =
            "## Chapter\n\n### Section A\n\n#### Detail\n\nText.\n\n" +
            "### Section B\n\nKeep.\n";
        const string after =
            "## Chapter\n\n### Section A\n\n#### Detail\n\nChanged.\n\n" +
            "### Section B\n\nKeep.\n";
        var beforeElements = Parse(before);
        var afterElements = Parse(after);

        foreach (var pointer in new[] { "1", "1.1", "1.1.1" })
        {
            var oldElement = beforeElements.Single(element => element.Pointer.Value == pointer);
            var newElement = afterElements.Single(element => element.Pointer.Value == pointer);
            Assert.Equal(oldElement.SelfHash, newElement.SelfHash);
            Assert.NotEqual(oldElement.SubtreeHash, newElement.SubtreeHash);
        }

        var oldSibling = beforeElements.Single(element => element.Pointer.Value == "1.2");
        var newSibling = afterElements.Single(element => element.Pointer.Value == "1.2");
        Assert.Equal(oldSibling.SelfHash, newSibling.SelfHash);
        Assert.Equal(oldSibling.SubtreeHash, newSibling.SubtreeHash);
    }

    [Fact]
    public void RawNonElementEdit_ChangesHeadingSubtreeHash()
    {
        const string before =
            "### Section A\n\nText.\n\n<!-- comment -->\n\n### Section B\n";
        const string after =
            "### Section A\n\nText.\n\n<!-- changed comment -->\n\n### Section B\n";

        var oldHeading = Parse(before).Single(element => element.Pointer.Value == "1");
        var newHeading = Parse(after).Single(element => element.Pointer.Value == "1");

        Assert.Equal(oldHeading.SelfHash, newHeading.SelfHash);
        Assert.NotEqual(oldHeading.SubtreeHash, newHeading.SubtreeHash);
    }

    [Fact]
    public void LeafElement_SubtreeHashEqualsSelfHash()
    {
        const string source =
            "---\ntitle: Test\n---\n\nParagraph.\n\n~~~csharp\nvar x = 1;\n~~~";
        var elements = Parse(source)
            .Where(element => element.Kind is
                MarkdownElementKind.FrontMatter or
                MarkdownElementKind.Paragraph or
                MarkdownElementKind.CodeBlock);

        Assert.All(elements, element =>
        {
            Assert.NotNull(element.SelfHash);
            Assert.Equal(element.SelfHash, element.SubtreeHash);
        });
    }

    [Fact]
    public void Resolver_PrefersDirectMatchAndRelocatesBySelfHashOnly()
    {
        const string text = "TODO";
        var selfHash = SemanticFingerprint.Compute(text);
        var anchor = new SemanticAnchor(
            new SemanticPointer("1.p1"),
            selfHash,
            "0000000000000000");
        var directCandidates = new[]
        {
            Candidate("1.p1", text),
            Candidate("1.p2", text)
        };

        Assert.Equal(
            "1.p1",
            SemanticAnchorResolver.Resolve(anchor, directCandidates).Value);

        var shiftedCandidates = new[]
        {
            Candidate("1.p1", "Inserted."),
            Candidate("1.p2", text)
        };
        Assert.Equal(
            "1.p2",
            SemanticAnchorResolver.Resolve(anchor, shiftedCandidates).Value);
    }

    [Fact]
    public void Resolver_RejectsChangedAndAmbiguousTargets()
    {
        var anchor = new SemanticAnchor(
            new SemanticPointer("1.p1"),
            SemanticFingerprint.Compute("Original."),
            "0000000000000000");

        var changed = Assert.Throws<SemanticAnchorConflictException>(
            () => SemanticAnchorResolver.Resolve(
                anchor,
                [Candidate("1.p1", "Changed.")]));
        Assert.Equal(SemanticAnchorConflictReason.SelfHashMismatch, changed.Reason);

        var duplicateHash = SemanticFingerprint.Compute("Original.");
        var ambiguous = Assert.Throws<SemanticAnchorConflictException>(
            () => SemanticAnchorResolver.Resolve(
                new SemanticAnchor(new SemanticPointer("1.p1"), duplicateHash),
                [
                    Candidate("1.p1", "Changed."),
                    Candidate("1.p2", "Original."),
                    Candidate("1.p3", "Original.")
                ]));
        Assert.Equal(
            SemanticAnchorConflictReason.AmbiguousSemanticPointer,
            ambiguous.Reason);
    }

    [Fact]
    public void AnchorFromElement_ProducesCanonicalV2AndMutationScopesAreCentralized()
    {
        const string source = "# Heading\n\nBody.";
        var elements = Parse(source);
        var heading = elements.Single(element => element.Pointer.Value == "1");
        var paragraph = elements.Single(element => element.Pointer.Value == "1.p1");

        Assert.Matches(
            @"^1~[0-9a-f]{16}~[0-9a-f]{16}$",
            SemanticAnchor.FromElement(heading).ToString());
        Assert.Matches(
            @"^1\.p1~([0-9a-f]{16})~\1$",
            SemanticAnchor.FromElement(paragraph).ToString());

        Assert.Equal(MutationScope.Subtree, PatchOperationKind.ReplaceSection.GetMutationScope());
        Assert.Equal(MutationScope.Self, PatchOperationKind.ReplaceElement.GetMutationScope());
        Assert.Equal(MutationScope.Self, PatchOperationKind.Replace.GetMutationScope());
        Assert.Equal(MutationScope.Self, PatchOperationKind.InsertBefore.GetMutationScope());
        Assert.Equal(MutationScope.Self, PatchOperationKind.InsertAfter.GetMutationScope());
        Assert.Equal(MutationScope.Self, PatchOperationKind.Delete.GetMutationScope());
    }

    private static IReadOnlyList<MarkdownElement> Parse(string source)
    {
        var document = new MarkdownSourceDocument(
            "a.md",
            "a.md",
            source,
            "hash",
            DateTimeOffset.UtcNow);
        return new MarkdownElementParser().Parse(document);
    }

    private static SemanticAnchorCandidate Candidate(string pointer, string text)
        => new(
            new SemanticPointer(pointer),
            MarkdownElementKind.Paragraph,
            text,
            SemanticFingerprint.Compute(text),
            SemanticFingerprint.Compute(text));
}
