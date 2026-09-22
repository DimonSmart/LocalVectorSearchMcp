using DimonSmart.LocalVectorSearchMcp.Core.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticElementHashing
{
    public static IReadOnlyList<MarkdownElement> Attach(
        string source,
        IReadOnlyList<MarkdownElement> elements)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(elements);

        var result = new List<MarkdownElement>(elements.Count);
        foreach (var element in elements)
        {
            if (element.Kind == MarkdownElementKind.Document || element.SourceLength <= 0)
            {
                result.Add(element with { SelfHash = null, SubtreeHash = null });
                continue;
            }

            var ownRange = new SourceRange(element.SourceStart, element.SourceLength);
            var selfHash = SemanticFingerprint.Compute(
                source.AsSpan(ownRange.Start, ownRange.Length));
            var subtreeRange = MarkdownOwnedSourceRange.GetOwnedSourceRange(
                source.Length,
                elements,
                element);
            var subtreeHash = subtreeRange == ownRange
                ? selfHash
                : SemanticFingerprint.Compute(
                    source.AsSpan(subtreeRange.Start, subtreeRange.Length));

            result.Add(element with
            {
                SelfHash = selfHash,
                SubtreeHash = subtreeHash
            });
        }

        return result;
    }
}
