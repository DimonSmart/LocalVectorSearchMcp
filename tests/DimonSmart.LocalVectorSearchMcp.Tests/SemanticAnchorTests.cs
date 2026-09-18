using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
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
    public void Fingerprint_UsesExactParserSourceSpan()
    {
        const string source = "# Heading\r\n\r\nParagraph.  \r\n";
        var document = new MarkdownSourceDocument(
            "a.md",
            "a.md",
            source,
            "hash",
            DateTimeOffset.UtcNow);
        var element = new MarkdownElementParser().Parse(document)
            .Single(item => item.Pointer.Value == "1.p1");
        var exactSpan = source.Substring(element.SourceStart, element.SourceLength);

        Assert.Equal(exactSpan, element.Text);
        Assert.Equal(
            SemanticFingerprint.Compute(exactSpan),
            SemanticFingerprint.Compute(element.Text));
    }

    [Fact]
    public void AnchorParser_AcceptsLogicalAndCanonicalizesFingerprint()
    {
        var logical = SemanticAnchorParser.Parse("1.2.p3");
        var fingerprinted = SemanticAnchorParser.Parse("1.2.p3~ABCDEF0123456789");

        Assert.Equal("1.2.p3", logical.LogicalPointer.Value);
        Assert.Null(logical.Fingerprint);
        Assert.Equal(
            "1.2.p3~abcdef0123456789",
            fingerprinted.ToString());
    }

    [Theory]
    [InlineData("1.p1~xyz")]
    [InlineData("1.p1~1234")]
    [InlineData("1.p1~0123456789abcdef~0")]
    [InlineData("document~0123456789abcdef")]
    public void AnchorParser_RejectsMalformedFingerprint(string value)
    {
        var exception = Assert.Throws<SemanticPointerFormatException>(
            () => SemanticAnchorParser.Parse(value));

        Assert.Equal("Invalid semantic pointer fingerprint.", exception.Message);
    }

    [Fact]
    public void Resolver_PrefersDirectMatchEvenWhenDuplicateExists()
    {
        const string text = "TODO";
        var fingerprint = SemanticFingerprint.Compute(text);
        var anchor = SemanticAnchorParser.Parse($"1.p1~{fingerprint}");
        var candidates = new[]
        {
            Candidate("1.p1", text),
            Candidate("1.p2", text)
        };

        var resolved = SemanticAnchorResolver.Resolve(anchor, candidates);

        Assert.Equal("1.p1", resolved.Value);
    }

    [Fact]
    public void Resolver_RelocatesUniqueElementAfterStructuralShift()
    {
        const string target = "Target.";
        var anchor = SemanticAnchorParser.Parse(
            $"1.p1~{SemanticFingerprint.Compute(target)}");
        var candidates = new[]
        {
            Candidate("1.p1", "Inserted."),
            Candidate("1.p2", target)
        };

        var resolved = SemanticAnchorResolver.Resolve(anchor, candidates);

        Assert.Equal("1.p2", resolved.Value);
    }

    [Fact]
    public void Resolver_RejectsChangedTargetWhenNoMatchRemains()
    {
        var anchor = SemanticAnchorParser.Parse(
            $"1.p1~{SemanticFingerprint.Compute("Original.")}");
        var candidates = new[]
        {
            Candidate("1.p1", "Changed.")
        };

        var exception = Assert.Throws<SemanticAnchorConflictException>(
            () => SemanticAnchorResolver.Resolve(anchor, candidates));

        Assert.Equal(
            "Pointer fingerprint does not match the current element.",
            exception.Message);
    }

    [Fact]
    public void Resolver_RejectsAmbiguousRelocation()
    {
        const string target = "TODO";
        var anchor = SemanticAnchorParser.Parse(
            $"1.p1~{SemanticFingerprint.Compute(target)}");
        var candidates = new[]
        {
            Candidate("1.p1", "Changed."),
            Candidate("1.p2", target),
            Candidate("1.p3", target)
        };

        var exception = Assert.Throws<SemanticAnchorConflictException>(
            () => SemanticAnchorResolver.Resolve(anchor, candidates));

        Assert.Equal(
            "The element fingerprint matches multiple current elements and cannot be relocated safely.",
            exception.Message);
    }

    [Fact]
    public void AnchorFromElement_UsesExactElementText()
    {
        const string source = "# Heading\n\nBody.";
        var document = new MarkdownSourceDocument(
            "a.md",
            "a.md",
            source,
            "hash",
            DateTimeOffset.UtcNow);
        var element = new MarkdownElementParser().Parse(document)
            .Single(item => item.Pointer.Value == "1.p1");

        var anchor = SemanticAnchor.FromElement(element);

        Assert.Equal("1.p1", anchor.LogicalPointer.Value);
        Assert.Equal(
            SemanticFingerprint.Compute("Body."),
            anchor.Fingerprint);
    }

    private static SemanticAnchorCandidate Candidate(
        string pointer,
        string text)
        => new(
            new SemanticPointer(pointer),
            MarkdownElementKind.Paragraph,
            text);
}
