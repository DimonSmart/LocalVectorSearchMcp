using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using Markdig;

using Markdig.Syntax;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

/// <summary>
/// Proves that a structured edit preserves surviving source blocks, their ownership,
/// and Markdig list container identity. Runs before the source is written.
/// </summary>
internal static class MarkdownStructuralEditValidator
{
    public static void Validate(
        string beforeSource,
        string afterSource,
        IReadOnlyList<MarkdownElement> before,
        IReadOnlyList<MarkdownElement> after,
        IReadOnlyList<PatchOperation> operations,
        IReadOnlyList<MarkdownSourceEdit> edits)
    {
        var orderedEdits = edits.OrderBy(edit => edit.Start).ToArray();
        var original = ReadBlocks(beforeSource);
        var updated = ReadBlocks(afterSource);
        var afterElementsByStart = after.Where(element =>
                element.Kind != MarkdownElementKind.Document)
            .ToLookup(element => (element.SourceStart, element.Kind));
        var afterBlocks = updated.ToLookup(block => (block.Start, block.Kind));
        var originalElements = before.ToDictionary(element => element.Pointer.Value,
            StringComparer.Ordinal);
        var currentElements = after.ToDictionary(element => element.Pointer.Value,
            StringComparer.Ordinal);
        var ownEditedListStarts = operations
            .Where(operation => operation.Kind is PatchOperationKind.Replace
                or PatchOperationKind.ReplaceElement)
            .Select(operation => originalElements.GetValueOrDefault(operation.Pointer))
            .Where(element => element?.Kind == MarkdownElementKind.ListItem)
            .Select(element => element!.SourceStart)
            .ToHashSet();

        int? MapOrigin(int position)
        {
            var delta = 0;
            foreach (var edit in orderedEdits)
            {
                if (edit.Length > 0 && position >= edit.Start
                    && position < edit.Start + edit.Length)
                    return position == edit.Start && ownEditedListStarts.Contains(position)
                        ? position + delta : null;
                if (edit.Start + edit.Length <= position)
                    delta += edit.Replacement.Length - edit.Length;
            }
            return position + delta;
        }

        // Re-index after parsing: semantic ordinals are intentionally not used as identity.
        foreach (var old in before.Where(element =>
            element.Kind != MarkdownElementKind.Document))
        {
            var position = MapOrigin(old.SourceStart);
            if (position is null || ownEditedListStarts.Contains(old.SourceStart)) continue;
            var candidates = afterElementsByStart[(position.Value, old.Kind)].ToArray();
            if (candidates.Length != 1
                || old.SelfHash != candidates[0].SelfHash
                || old.Text != candidates[0].Text)
                throw Conflict(old.Pointer.Value, "surviving element text or kind changed");

            if (old.Kind != MarkdownElementKind.ListItem) continue;
            var current = candidates[0];
            if (old.SourceMap is null || current.SourceMap is null
                || old.SourceMap.Depth != current.SourceMap.Depth
                || old.SourceMap.MarkerStyle != current.SourceMap.MarkerStyle)
                throw Conflict(old.Pointer.Value, "list depth or marker changed");

            int? ParentPosition(MarkdownElement child,
                IReadOnlyDictionary<string, MarkdownElement> lookup, bool map)
            {
                var parent = child.SourceMap?.ParentPointer;
                if (parent is null) return null;
                if (!lookup.TryGetValue(parent, out var element))
                    throw Conflict(child.Pointer.Value, "list parent cannot be resolved");
                return map ? MapOrigin(element.SourceStart) : element.SourceStart;
            }

            var originalParent = ParentPosition(old, originalElements, true);
            var currentParent = ParentPosition(current, currentElements, false);
            if (originalParent != currentParent)
                throw Conflict(old.Pointer.Value, "surviving list item was reparented");
        }

        // Include unaddressed Markdig leaf blocks: HTML, link definitions,
        // thematic breaks, paragraphs hidden within list items, etc.
        foreach (var old in original.Where(block => block.Block is not ContainerBlock))
        {
            if (orderedEdits.Any(edit => Intersects(old.Range, edit))) continue;
            var position = MapOrigin(old.Start);
            if (position is null) continue;
            var matches = afterBlocks[(position.Value, old.Kind)].ToArray();
            if (matches.Length != 1)
                throw Conflict(old.Kind, "surviving Markdown block changed kind or ownership");
            var current = matches[0];
            var oldText = beforeSource.Substring(old.Range.Start, old.Range.Length);
            var newText = afterSource.Substring(current.Range.Start, current.Range.Length);
            if (oldText != newText)
                throw Conflict(old.Kind, "unmodified Markdown block text changed");
            var originalParents = StructuralParents(old.Block, beforeSource);
            var newParents = StructuralParents(current.Block, afterSource);
            if (originalParents.Count != newParents.Count)
                throw Conflict(old.Kind, "surviving block was absorbed by a container");
            for (var index = 0; index < originalParents.Count; index++)
            {
                if (originalParents[index].Kind != newParents[index].Kind
                    || MapOrigin(originalParents[index].Start) != newParents[index].Start)
                    throw Conflict(old.Kind, "surviving block changed list or quote parent");
            }
        }

        // A list's own Span.Start is not stable. Match its surviving direct items
        // instead and require a bijection between original and resulting containers.
        var containerForward = new Dictionary<ListBlock, ListBlock>(
            ReferenceEqualityComparer.Instance);
        var containerReverse = new Dictionary<ListBlock, ListBlock>(
            ReferenceEqualityComparer.Instance);
        foreach (var old in original.Where(block => block.Block is ListItemBlock))
        {
            var position = MapOrigin(old.Start);
            if (position is null) continue;
            var matches = afterBlocks[(position.Value, nameof(ListItemBlock))].ToArray();
            if (matches.Length != 1
                || old.Block.Parent is not ListBlock oldList
                || matches[0].Block.Parent is not ListBlock newList)
                throw Conflict("list", "surviving list item changed container");
            if (containerForward.TryGetValue(oldList, out var existing)
                && !ReferenceEquals(existing, newList)
                || containerReverse.TryGetValue(newList, out var previous)
                && !ReferenceEquals(previous, oldList))
                throw Conflict("list", "a list container was split or merged");
            containerForward[oldList] = newList;
            containerReverse[newList] = oldList;
        }

        // Every new root must be the *only* newly introduced sibling or quote.
        // Inspect the raw AST, not just addressable elements: a detached HTML
        // comment cannot masquerade as part of a valid fragment.
        foreach (var operation in operations)
        {
            if (!originalElements.TryGetValue(operation.Pointer, out var target)
                || target.Kind is not (MarkdownElementKind.ListItem or MarkdownElementKind.BlockQuote)
                || operation.Kind == PatchOperationKind.Delete)
                continue;

            var edit = orderedEdits.Single(item => item.Pointer == operation.Pointer);
            var precedingDelta = orderedEdits
                .Where(item => item.Start < edit.Start)
                .Sum(item => item.Replacement.Length - item.Length);
            var start = edit.Start + precedingDelta;
            var end = start + edit.Replacement.Length;
            var roots = after.Where(element => element.Kind == target.Kind
                && element.SourceStart >= start && element.SourceStart < end
                && (target.Kind == MarkdownElementKind.BlockQuote
                    || element.SourceMap?.Depth == target.SourceMap?.Depth))
                .ToArray();
            if (roots.Length != 1)
                throw Conflict(operation.Pointer, "exactly one structured root is required");
            var root = roots[0];
            var rootBlockType = target.Kind == MarkdownElementKind.ListItem
                ? nameof(ListItemBlock) : nameof(QuoteBlock);
            var astRoots = afterBlocks[(root.SourceStart, rootBlockType)].ToArray();
            if (astRoots.Length != 1)
                throw Conflict(operation.Pointer, "structured root has ambiguous Markdig ownership");
            var rootBlock = astRoots[0].Block;

            if (target.Kind == MarkdownElementKind.ListItem)
            {
                if (root.SourceMap?.MarkerStyle != target.SourceMap?.MarkerStyle
                    || root.SourceMap?.Indent != target.SourceMap?.Indent)
                    throw Conflict(operation.Pointer, "replacement list marker or indentation changed");
                var expectedParent = target.SourceMap?.ParentPointer;
                var existingParent = expectedParent is null ? null
                    : originalElements[expectedParent];
                var mappedParent = existingParent is null ? null
                    : MapOrigin(existingParent.SourceStart);
                int? actualParent = root.SourceMap.ParentPointer is null ? null
                    : currentElements[root.SourceMap.ParentPointer].SourceStart;
                if (mappedParent != actualParent)
                    throw Conflict(operation.Pointer, "new item has the wrong list parent");

                var targetPosition = MapOrigin(target.SourceStart);
                if (operation.Kind is PatchOperationKind.InsertBefore or PatchOperationKind.InsertAfter)
                {
                    var sibling = updated.SingleOrDefault(block =>
                        block.Block is ListItemBlock && block.Start == targetPosition);
                    if (sibling?.Block.Parent is not ListBlock list
                        || !ReferenceEquals(rootBlock.Parent, list))
                        throw Conflict(operation.Pointer, "new sibling is in a different list container");
                }
                else
                {
                    var oldItem = original.SingleOrDefault(block =>
                        block.Block is ListItemBlock && block.Start == target.SourceStart);
                    if (oldItem?.Block.Parent is not ListBlock oldList)
                        throw Conflict(operation.Pointer, "original list container is ambiguous");
                    if (containerForward.TryGetValue(oldList, out var preservedList)
                        && !ReferenceEquals(rootBlock.Parent, preservedList))
                        throw Conflict(operation.Pointer, "replacement split the original list");
                }
            }

            foreach (var block in updated.Where(item =>
                item.Start >= start && item.Start < end
                && (item.Block is not ContainerBlock
                    || item.Block is ListItemBlock or QuoteBlock)))
            {
                if (!ReferenceEquals(block.Block, rootBlock)
                    && !IsDescendantOf(block.Block, rootBlock))
                    throw Conflict(operation.Pointer,
                        $"fragment introduces an unrelated {block.Kind} outside the new subtree");
            }

            // The inserted non-whitespace source must belong to the new root.
            // Separators may lie outside it, but no new independent content may.
            var ownedRange = MarkdownSourceMapBuilder.GetPhysicalRange(rootBlock, afterSource);
            for (var offset = start; offset < end; offset++)
            {
                if (!char.IsWhiteSpace(afterSource[offset])
                    && (offset < ownedRange.Start || offset >= ownedRange.End))
                    throw Conflict(operation.Pointer,
                        "fragment contains Markdown outside the new structured root");
            }
        }
    }

    private static bool Intersects(SourceRange range, MarkdownSourceEdit edit)
        => edit.Length > 0 && edit.Start < range.End
            && edit.Start + edit.Length > range.Start;

    private static bool IsDescendantOf(Block block, Block ancestor)
    {
        for (var current = block.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static IReadOnlyList<(string Kind, int Start)> StructuralParents(
        Block block, string source)
    {
        var result = new List<(string Kind, int Start)>();
        for (var current = block.Parent; current is not null; current = current.Parent)
            if (current is ListItemBlock or QuoteBlock)
                result.Add((current.GetType().Name,
                    MarkdownSourceMapBuilder.GetLineStart(source,
                        Math.Clamp(current.Span.Start, 0, source.Length))));
        result.Reverse();
        return result;
    }

    private static IReadOnlyList<BlockInfo> ReadBlocks(string source)
    {
        var document = Markdig.Markdown.Parse(source, MarkdownPipelines.Tables);
        return document.Descendants().OfType<Block>()
            .Where(block => block.Span.Start >= 0)
            .Select(block => new BlockInfo(block,
                MarkdownSourceMapBuilder.GetPhysicalRange(block, source)))
            .ToArray();
    }

    private static WorkspaceMutationException Conflict(string pointer, string reason)
        => new($"Unsupported Markdown structure at '{pointer}': {reason}.");

    private sealed record BlockInfo(Block Block, SourceRange Range)
    {
        public string Kind => Block.GetType().Name;
        public int Start => Range.Start;
    }
}
