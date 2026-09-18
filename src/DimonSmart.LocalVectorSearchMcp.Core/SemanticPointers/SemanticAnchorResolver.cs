using DimonSmart.LocalVectorSearchMcp.Core.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticAnchorResolver
{
    public static SemanticPointer Resolve(
        SemanticAnchor anchor,
        IReadOnlyList<SemanticAnchorCandidate> elements)
    {
        if (anchor.IsDocument || anchor.Fingerprint is null)
        {
            return anchor.LogicalPointer;
        }

        var expectedKind = GetExpectedElementKind(anchor.LogicalPointer);
        var direct = elements.FirstOrDefault(element =>
            element.Pointer.Value.Equals(anchor.LogicalPointer.Value, StringComparison.Ordinal));

        if (direct is not null
            && direct.Kind == expectedKind
            && FingerprintMatches(direct.Text, anchor.Fingerprint))
        {
            return direct.Pointer;
        }

        var candidates = elements
            .Where(element => element.Kind == expectedKind
                && FingerprintMatches(element.Text, anchor.Fingerprint))
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0].Pointer,
            > 1 => throw new SemanticAnchorConflictException(
                "The element fingerprint matches multiple current elements and cannot be relocated safely."),
            _ when direct is not null && direct.Kind == expectedKind
                => throw new SemanticAnchorConflictException(
                    "Pointer fingerprint does not match the current element."),
            _ => throw new SemanticAnchorConflictException(
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
            _ => throw new SemanticPointerFormatException($"Invalid semantic pointer: {pointer.Value}")
        };

    private static bool FingerprintMatches(string text, string expected)
        => SemanticFingerprint.Compute(text).Equals(expected, StringComparison.OrdinalIgnoreCase);
}

public sealed record SemanticAnchorCandidate(
    SemanticPointer Pointer,
    MarkdownElementKind Kind,
    string Text);
