using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class MarkdownImageReferenceUpdater(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard) : IMarkdownImageReferenceUpdater
{
    public MarkdownImageReferenceUpdate Update(
        string markdown,
        string documentPath,
        string sourcePath,
        string targetPath)
    {
        var documentAbsolute = pathGuard.ResolveMarkdownPath(documentPath);
        var sourceAbsolute = pathGuard.ResolveWorkspaceImagePath(sourcePath);
        var targetAbsolute = pathGuard.ResolveWorkspaceImagePath(targetPath);
        var documentDirectory = Path.GetDirectoryName(documentAbsolute)!;
        var syntax = Markdown.Parse(markdown);
        var replacements = new List<Replacement>();

        foreach (var link in syntax.Descendants().OfType<LinkInline>())
        {
            if (!link.IsImage
                || !TryGetDestination(markdown, link, out var destination))
            {
                continue;
            }

            var localPath = UnescapeMarkdownPath(destination.Value);
            if (!TryResolveLocalPath(
                    documentDirectory,
                    localPath,
                    out var resolved)
                || !resolved.Equals(sourceAbsolute, PathComparison()))
            {
                continue;
            }

            var relative = Path.GetRelativePath(
                    documentDirectory,
                    targetAbsolute)
                .Replace('\\', '/');
            if (destination.Value.StartsWith("./", StringComparison.Ordinal)
                && !relative.StartsWith(".", StringComparison.Ordinal))
            {
                relative = "./" + relative;
            }

            var useAngles = destination.AngleWrapped
                || relative.Any(char.IsWhiteSpace)
                || relative.Contains('(')
                || relative.Contains(')');
            var replacement = useAngles
                ? $"<{EscapeAnglePath(relative)}>"
                : relative;
            replacements.Add(new Replacement(
                destination.Start,
                destination.Length,
                replacement));
        }

        if (replacements.Count == 0)
        {
            return new MarkdownImageReferenceUpdate(markdown, 0);
        }

        var updated = markdown;
        foreach (var replacement in replacements
                     .OrderByDescending(item => item.Start))
        {
            updated = updated.Remove(
                    replacement.Start,
                    replacement.Length)
                .Insert(
                    replacement.Start,
                    replacement.Value);
        }

        return new MarkdownImageReferenceUpdate(
            updated,
            replacements.Count);
    }

    private bool TryResolveLocalPath(
        string documentDirectory,
        string destination,
        out string resolved)
    {
        resolved = "";
        if (string.IsNullOrWhiteSpace(destination)
            || destination.StartsWith("#", StringComparison.Ordinal)
            || destination.StartsWith("//", StringComparison.Ordinal)
            || Path.IsPathRooted(destination)
            || destination.Contains('?')
            || destination.Contains('#')
            || Uri.TryCreate(
                destination,
                UriKind.Absolute,
                out var absoluteUri)
                && absoluteUri.IsAbsoluteUri)
        {
            return false;
        }

        try
        {
            resolved = Path.GetFullPath(Path.Combine(
                documentDirectory,
                destination.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(config.KnowledgeBase.Root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            return resolved.Equals(root, PathComparison())
                || resolved.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    PathComparison());
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryGetDestination(
        string markdown,
        LinkInline link,
        out Destination destination)
    {
        destination = default;
        var start = Math.Max(0, link.Span.Start);
        var end = Math.Min(markdown.Length - 1, link.Span.End);
        if (start >= markdown.Length || end < start)
        {
            return false;
        }

        var openParen = -1;
        for (var index = end; index > start; index--)
        {
            if (markdown[index] != '(')
            {
                continue;
            }

            var previous = index - 1;
            while (previous >= start
                   && markdown[previous] is ' ' or '\t')
            {
                previous--;
            }

            if (previous >= start && markdown[previous] == ']')
            {
                openParen = index;
                break;
            }
        }

        if (openParen < 0)
        {
            return false;
        }

        var cursor = openParen + 1;
        while (cursor <= end
               && markdown[cursor] is ' ' or '\t' or '\r' or '\n')
        {
            cursor++;
        }

        if (cursor > end)
        {
            return false;
        }

        if (markdown[cursor] == '<')
        {
            var tokenStart = cursor;
            cursor++;
            var valueStart = cursor;
            while (cursor <= end)
            {
                if (markdown[cursor] == '\\')
                {
                    cursor += 2;
                    continue;
                }

                if (markdown[cursor] == '>')
                {
                    destination = new Destination(
                        tokenStart,
                        cursor - tokenStart + 1,
                        markdown[valueStart..cursor],
                        true);
                    return true;
                }

                cursor++;
            }

            return false;
        }

        var bareStart = cursor;
        var depth = 0;
        while (cursor <= end)
        {
            var character = markdown[cursor];
            if (character == '\\')
            {
                cursor += 2;
                continue;
            }

            if (character == '(')
            {
                depth++;
                cursor++;
                continue;
            }

            if (character == ')')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                cursor++;
                continue;
            }

            if (depth == 0 && char.IsWhiteSpace(character))
            {
                break;
            }

            cursor++;
        }

        if (cursor <= bareStart)
        {
            return false;
        }

        destination = new Destination(
            bareStart,
            cursor - bareStart,
            markdown[bareStart..cursor],
            false);
        return true;
    }

    private static string UnescapeMarkdownPath(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\'
                && index + 1 < value.Length)
            {
                result.Append(value[++index]);
                continue;
            }

            result.Append(value[index]);
        }

        return result.ToString();
    }

    private static string EscapeAnglePath(string value)
        => value.Replace(">", "\\>", StringComparison.Ordinal);

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly record struct Destination(
        int Start,
        int Length,
        string Value,
        bool AngleWrapped);

    private readonly record struct Replacement(
        int Start,
        int Length,
        string Value);
}
