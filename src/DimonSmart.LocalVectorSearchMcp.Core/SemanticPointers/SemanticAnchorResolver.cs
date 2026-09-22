using DimonSmart.LocalVectorSearchMcp.Core.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticAnchorResolver
{
    public static SemanticPointer Resolve(
        SemanticAnchor anchor,
        IReadOnlyList<SemanticAnchorCandidate> elements)
    {
        if (anchor.IsDocument || anchor.SelfHash is null)
        {
            return anchor.LogicalPointer;
        }

        return ResolveCandidate(anchor, elements).Pointer;
    }

    public static SemanticAnchorCandidate ResolveCandidate(
        SemanticAnchor anchor,
        IReadOnlyList<SemanticAnchorCandidate> elements)
    {
        if (anchor.IsDocument || anchor.SelfHash is null)
        {
            throw new SemanticPointerFormatException(
                "A concrete semantic anchor with self hash is required.");
        }

        var expectedKind = GetExpectedElementKind(anchor.LogicalPointer);
        var direct = elements.FirstOrDefault(element =>
            element.Pointer.Value.Equals(
                anchor.LogicalPointer.Value,
                StringComparison.Ordinal));

        if (direct is not null
            && direct.Kind == expectedKind
            && SelfHashMatches(direct, anchor.SelfHash))
        {
            return direct;
        }

        var candidates = elements
            .Where(element => element.Kind == expectedKind
                && SelfHashMatches(element, anchor.SelfHash))
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            > 1 => throw new SemanticAnchorConflictException(
                SemanticAnchorConflictReason.AmbiguousSemanticPointer,
                "The element fingerprint matches multiple current elements and cannot be relocated safely."),
            _ when direct is not null && direct.Kind == expectedKind
                => throw new SemanticAnchorConflictException(
                    SemanticAnchorConflictReason.SelfHashMismatch,
                    "Pointer fingerprint does not match the current element."),
            _ => throw new SemanticAnchorConflictException(
                SemanticAnchorConflictReason.SemanticTargetNotFound,
                "The original element could not be found in the current document.")
        };
    }

    public static MarkdownElementKind GetExpectedElementKind(SemanticPointer pointer)
        => SemanticPointerParser.GetKind(pointer) switch
        {
            SemanticPointerKind.Document => MarkdownElementKind.Document,
            SemanticPointerKind.FrontMatter => MarkdownElementKind.FrontMatter,
            SemanticPointerKind.Section => MarkdownElementKind.Heading,
            SemanticPointerKind.Paragraph => MarkdownElementKind.Paragraph,
            SemanticPointerKind.CodeBlock => MarkdownElementKind.CodeBlock,
            _ => throw new SemanticPointerFormatException(
                $"Invalid semantic pointer: {pointer.Value}")
        };

    private static bool SelfHashMatches(
        SemanticAnchorCandidate candidate,
        string expected)
        => (candidate.SelfHash ?? SemanticFingerprint.Compute(candidate.Text))
            .Equals(expected, StringComparison.OrdinalIgnoreCase);
}

public sealed record SemanticAnchorCandidate(
    SemanticPointer Pointer,
    MarkdownElementKind Kind,
    string Text,
    string? SelfHash = null,
    string? SubtreeHash = null);
