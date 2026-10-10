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

        var parentsWithChildren = elements
            .Where(element => element.Kind == MarkdownElementKind.ListItem
                && element.SourceMap?.ParentPointer is not null)
            .Select(element => element.SourceMap!.ParentPointer!)
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<MarkdownElement>(elements.Count);
        foreach (var element in elements)
        {
            if (element.Kind == MarkdownElementKind.Document || element.SourceLength <= 0)
            {
                result.Add(element with { SelfHash = null, SubtreeHash = null });
                continue;
            }

            var ownRange = new SourceRange(element.SourceStart, element.SourceLength);
            var selfHash = element.Kind == MarkdownElementKind.ListItem
                && element.SourceMap is not null
                    ? SemanticFingerprint.ComputeSegments(source, element.SourceMap.OwnSegments)
                    : SemanticFingerprint.Compute(
                        source.AsSpan(ownRange.Start, ownRange.Length));
            var subtreeRange = MarkdownOwnedSourceRange.GetOwnedSourceRange(
                source.Length,
                elements,
                element);
            var subtreeHash = element.Kind == MarkdownElementKind.BlockQuote
                || (element.Kind == MarkdownElementKind.ListItem
                    && element.SourceMap is not null
                    && !parentsWithChildren.Contains(element.Pointer.Value))
                || (element.Kind != MarkdownElementKind.ListItem && subtreeRange == ownRange)
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
