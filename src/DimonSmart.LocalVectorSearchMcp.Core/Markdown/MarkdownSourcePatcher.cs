using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public static class MarkdownSourcePatcher
{
    public static string Apply(
        string source,
        IReadOnlyList<MarkdownElement> elements,
        IReadOnlyList<PatchOperation> operations)
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
            if (!byPointer.TryGetValue(operation.Pointer, out var element))
            {
                throw new WorkspaceMutationException(
                    $"Pointer '{operation.Pointer}' was not found in the requested document revision.");
            }

            var markdown = NormalizeLineEndings(operation.Markdown ?? "", eol);
            var edit = operation.Kind switch
            {
                PatchOperationKind.Replace when operation.Markdown is not null
                    => new SourceEdit(element.SourceStart, element.SourceLength, markdown, operation.Pointer),
                PatchOperationKind.InsertBefore when operation.Markdown is not null
                    => new SourceEdit(element.SourceStart, 0, markdown.TrimEnd('\r', '\n') + eol + eol, operation.Pointer),
                PatchOperationKind.InsertAfter when operation.Markdown is not null
                    => new SourceEdit(element.SourceStart + element.SourceLength, 0, eol + eol + markdown.TrimStart('\r', '\n'), operation.Pointer),
                PatchOperationKind.Delete
                    => new SourceEdit(element.SourceStart, element.SourceLength, "", operation.Pointer),
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

        var result = source;
        foreach (var edit in ordered.OrderByDescending(edit => edit.Start))
        {
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        }

        return result;
    }

    private static string DetectLineEnding(string source)
        => source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string NormalizeLineEndings(string value, string eol)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", eol, StringComparison.Ordinal);

    private sealed record SourceEdit(int Start, int Length, string Replacement, string Pointer);
}
