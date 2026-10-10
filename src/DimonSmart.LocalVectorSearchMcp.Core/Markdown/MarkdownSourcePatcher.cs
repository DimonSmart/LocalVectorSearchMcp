using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public static class MarkdownSourcePatcher
{
    public static string Apply(
        string source,
        IReadOnlyList<MarkdownElement> elements,
        IReadOnlyList<PatchOperation> operations)
        => Apply(source, elements, operations, out _);

    public static string Apply(
        string source,
        IReadOnlyList<MarkdownElement> elements,
        IReadOnlyList<PatchOperation> operations,
        out IReadOnlyList<MarkdownSourceEdit> plannedEdits)
    {
        if (operations.Count == 0)
        {
            throw new WorkspaceMutationException("At least one patch operation is required.");
        }

        var byPointer = elements
            .Where(element => element.SourceLength > 0)
            .ToDictionary(element => element.Pointer.Value, StringComparer.Ordinal);
        var duplicate = operations.GroupBy(operation => operation.Pointer, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WorkspaceMutationException(
                $"Patch operations conflict at pointer '{duplicate.Key}'.");
        }

        var eol = DetectLineEnding(source);
        var edits = new List<SourceEdit>(operations.Count);
        foreach (var operation in operations)
        {
            if (operation.Pointer == "document")
            {
                edits.Add(CreateDocumentEdit(source, operation, eol));
                continue;
            }

            if (!byPointer.TryGetValue(operation.Pointer, out var element))
            {
                throw new WorkspaceMutationException(
                    $"Pointer '{operation.Pointer}' was not found in the requested document revision.");
            }

            var markdown = NormalizeLineEndings(operation.Markdown ?? "", eol);
            var edit = element.Kind is MarkdownElementKind.ListItem or MarkdownElementKind.BlockQuote
                ? CreateStructuredEdit(source, element, operation, markdown, eol)
                : operation.Kind switch
                {
                    PatchOperationKind.Replace or PatchOperationKind.ReplaceElement
                        when operation.Markdown is not null
                        => new SourceEdit(
                            element.SourceStart,
                            element.SourceLength,
                            markdown,
                            operation.Pointer),
                    PatchOperationKind.ReplaceSection
                        when operation.Markdown is not null
                        => CreateSectionEdit(
                            source,
                            elements,
                            element,
                            markdown,
                            operation.Pointer,
                            eol,
                            "replace_section",
                            preserveBoundarySeparator: true),
                    PatchOperationKind.DeleteSection
                        when operation.Markdown is null
                        => CreateSectionEdit(
                            source,
                            elements,
                            element,
                            "",
                            operation.Pointer,
                            eol,
                            "delete_section",
                            preserveBoundarySeparator: false),
                    PatchOperationKind.DeleteSection
                        => throw new WorkspaceMutationException(
                            "delete_section does not accept markdown content."),
                    PatchOperationKind.InsertBefore when operation.Markdown is not null
                        => new SourceEdit(
                            element.SourceStart,
                            0,
                            markdown.TrimEnd('\r', '\n') + eol + eol,
                            operation.Pointer),
                    PatchOperationKind.InsertAfter when operation.Markdown is not null
                        => new SourceEdit(
                            element.SourceStart + element.SourceLength,
                            0,
                            eol + eol + markdown.TrimStart('\r', '\n'),
                            operation.Pointer),
                    PatchOperationKind.Delete
                        => new SourceEdit(
                            element.SourceStart,
                            element.SourceLength,
                            "",
                            operation.Pointer),
                    _ => throw new WorkspaceMutationException(
                        $"Patch operation '{operation.Kind}' requires markdown content.")
                };
            edits.Add(edit);
        }

        var ordered = edits.OrderBy(edit => edit.Start).ThenBy(edit => edit.Length).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (current.Start < previous.Start + previous.Length
                || current.Start == previous.Start)
            {
                throw new WorkspaceMutationException(
                    $"Patch operations at '{previous.Pointer}' and '{current.Pointer}' overlap.");
            }
        }

        plannedEdits = ordered
            .Select(edit => new MarkdownSourceEdit(edit.Start, edit.Length, edit.Replacement, edit.Pointer))
            .ToArray();

        var result = source;
        foreach (var edit in ordered.OrderByDescending(edit => edit.Start))
        {
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        }

        return result;
    }

    private static SourceEdit CreateStructuredEdit(
        string source,
        MarkdownElement target,
        PatchOperation operation,
        string markdown,
        string eol)
    {
        var map = target.SourceMap ?? throw new WorkspaceMutationException(
            "Unsupported list container: exact source ownership is not available.");
        var quote = target.Kind == MarkdownElementKind.BlockQuote;
        var fragment = markdown.TrimEnd('\r', '\n');

        if (operation.Kind is PatchOperationKind.ReplaceSection or PatchOperationKind.DeleteSection)
            throw new WorkspaceMutationException(
                "Section operations require a heading pointer.");

        if (!quote && (map.MarkerStyle is null || map.Indent < 0))
            throw new WorkspaceMutationException(
                $"Unsupported list container at '{target.Pointer.Value}': marker or indentation is ambiguous.");

        return operation.Kind switch
        {
            PatchOperationKind.Delete when operation.Markdown is null
                => new SourceEdit(map.DeleteRange.Start, map.DeleteRange.Length,
                    "", operation.Pointer),
            PatchOperationKind.Delete => throw new WorkspaceMutationException(
                "delete does not accept markdown content for list items or quotes."),
            PatchOperationKind.ReplaceSubtree when !quote && operation.Markdown is not null
                => new SourceEdit(map.ReplaceRange.Start, map.ReplaceRange.Length,
                    fragment, operation.Pointer),
            PatchOperationKind.Replace or PatchOperationKind.ReplaceElement
                when operation.Markdown is not null && !quote
                => CreateListOwnEdit(source, map, fragment, operation.Pointer),
            PatchOperationKind.Replace or PatchOperationKind.ReplaceElement
                when operation.Markdown is not null
                => new SourceEdit(map.ReplaceRange.Start, map.ReplaceRange.Length,
                    fragment, operation.Pointer),
            PatchOperationKind.InsertBefore when operation.Markdown is not null
                => new SourceEdit(map.BeforePosition, 0,
                    fragment + (quote ? eol + eol : eol), operation.Pointer),
            PatchOperationKind.InsertAfter when operation.Markdown is not null
                => new SourceEdit(map.AfterPosition, 0,
                    quote
                        ? GetQuoteInsertAfter(source, map.AfterPosition, fragment, eol)
                        : GetListInsertAfter(source, map.AfterPosition, fragment, eol),
                    operation.Pointer),
            _ => throw new WorkspaceMutationException(
                $"Patch operation '{operation.Kind}' requires markdown content.")
        };
    }

    private static SourceEdit CreateListOwnEdit(
        string source, MarkdownElementSourceMap map, string fragment, string pointer)
    {
        // OwnSegments excludes child item spans. Only the prefix before the first
        // nested item is contiguous; edit that prefix and leave descendants and
        // any parent-owned suffix after descendants byte-for-byte unchanged.
        if (map.OwnSegments.Count == 0
            || map.OwnSegments[0].Start != map.SubtreeRange.Start)
            throw new WorkspaceMutationException(
                $"Unsupported list container at '{pointer}': own source prefix is ambiguous.");

        var prefix = map.OwnSegments[0];
        var separatorStart = prefix.End;
        while (separatorStart > prefix.Start
            && source[separatorStart - 1] is '\r' or '\n')
            separatorStart--;

        // Blank lines directly before a nested list are formatting boundaries,
        // not part of the prose being rewritten. Preserve them as well.
        var separator = source[separatorStart..prefix.End];
        return new SourceEdit(prefix.Start, prefix.Length, fragment + separator, pointer);
    }

    private static string GetListInsertAfter(string source, int position,
        string fragment, string eol)
    {
        var prefix = position > 0 && source[position - 1] != '\n' ? eol : "";
        var suffix = position < source.Length ? eol : "";
        return prefix + fragment + suffix;
    }

    private static string GetQuoteInsertAfter(string source, int position,
        string fragment, string eol)
    {
        var prefix = position > 0 && source[position - 1] == '\n'
            ? eol : eol + eol;
        var suffix = position < source.Length
            ? (source.AsSpan(position).StartsWith(eol, StringComparison.Ordinal)
                ? eol : eol + eol)
            : "";
        return prefix + fragment + suffix;
    }

    private static SourceEdit CreateSectionEdit(
        string source,
        IReadOnlyList<MarkdownElement> elements,
        MarkdownElement target,
        string markdown,
        string pointer,
        string eol,
        string operationName,
        bool preserveBoundarySeparator)
    {
        if (target.Kind != MarkdownElementKind.Heading)
        {
            throw new WorkspaceMutationException(
                $"{operationName} requires a heading pointer.");
        }

        var range = MarkdownOwnedSourceRange.GetOwnedSourceRange(
            source.Length,
            elements,
            target);
        var replacement = preserveBoundarySeparator && range.End != source.Length
            ? EnsureTrailingBlockSeparator(markdown, eol)
            : markdown;

        return new SourceEdit(
            range.Start,
            range.Length,
            replacement,
            pointer);
    }

    private static SourceEdit CreateDocumentEdit(
        string source,
        PatchOperation operation,
        string eol)
    {
        if (operation.Kind is PatchOperationKind.Replace
            or PatchOperationKind.ReplaceElement
            or PatchOperationKind.ReplaceSection
            or PatchOperationKind.DeleteSection
            or PatchOperationKind.Delete)
        {
            throw new WorkspaceMutationException(
                $"Operation '{GetKindName(operation.Kind)}' is not supported for the document pointer.");
        }

        if (operation.Markdown is null)
        {
            throw new WorkspaceMutationException(
                $"Patch operation '{operation.Kind}' requires markdown content.");
        }

        var markdown = NormalizeLineEndings(operation.Markdown, eol);
        if (source.Length == 0)
        {
            return new SourceEdit(0, 0, markdown, operation.Pointer);
        }

        return operation.Kind switch
        {
            PatchOperationKind.InsertBefore
                => new SourceEdit(
                    0,
                    0,
                    markdown.TrimEnd('\r', '\n') + GetLeadingBlockSeparator(source, eol),
                    operation.Pointer),
            PatchOperationKind.InsertAfter
                => new SourceEdit(
                    source.Length,
                    0,
                    GetTrailingBlockSeparator(source, eol) + markdown.TrimStart('\r', '\n'),
                    operation.Pointer),
            _ => throw new WorkspaceMutationException(
                $"Operation '{GetKindName(operation.Kind)}' is not supported for the document pointer.")
        };
    }

    private static string EnsureTrailingBlockSeparator(string markdown, string eol)
    {
        if (markdown.EndsWith(eol + eol, StringComparison.Ordinal))
        {
            return markdown;
        }

        return markdown.EndsWith(eol, StringComparison.Ordinal)
            ? markdown + eol
            : markdown + eol + eol;
    }

    private static string GetLeadingBlockSeparator(string source, string eol)
    {
        if (source.StartsWith(eol + eol, StringComparison.Ordinal)) return "";
        return source.StartsWith(eol, StringComparison.Ordinal) ? eol : eol + eol;
    }

    private static string GetTrailingBlockSeparator(string source, string eol)
    {
        if (source.EndsWith(eol + eol, StringComparison.Ordinal)) return "";
        return source.EndsWith(eol, StringComparison.Ordinal) ? eol : eol + eol;
    }

    private static string DetectLineEnding(string source)
        => source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string NormalizeLineEndings(string value, string eol)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", eol, StringComparison.Ordinal);

    private static string GetKindName(PatchOperationKind kind)
        => kind switch
        {
            PatchOperationKind.Replace => "replace",
            PatchOperationKind.ReplaceElement => "replace_element",
            PatchOperationKind.ReplaceSubtree => "replace_subtree",
            PatchOperationKind.ReplaceSection => "replace_section",
            PatchOperationKind.DeleteSection => "delete_section",
            PatchOperationKind.InsertBefore => "insert_before",
            PatchOperationKind.InsertAfter => "insert_after",
            PatchOperationKind.Delete => "delete",
            _ => kind.ToString()
        };

    private sealed record SourceEdit(int Start, int Length, string Replacement, string Pointer);
}

public sealed record MarkdownSourceEdit(int Start, int Length, string Replacement, string Pointer);
