using System.Text.RegularExpressions;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed partial class WorkspaceNavigationService(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard,
    IMarkdownDocumentLoader loader,
    IMarkdownElementParser parser) : IWorkspaceNavigationService
{
    public Task<WorkspaceFileList> ListFilesAsync(
        string? pathPrefix,
        string? includeGlob,
        CancellationToken cancellationToken)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(pathPrefix)
            ? null
            : pathGuard.ValidateRelativePath(pathPrefix).TrimEnd('/');
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(string.IsNullOrWhiteSpace(includeGlob) ? "**/*" : includeGlob);
        var storagePath = Path.GetFullPath(config.Storage.Path);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };
        var files = new List<WorkspaceFile>();
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(config.KnowledgeBase.Root));

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.GetFileSystemInfos("*", options);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException
                    or DirectoryNotFoundException
                    or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Push(childDirectory);
                    continue;
                }

                if (entry is not FileInfo info)
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(info.FullName);
                if (fullPath.Equals(storagePath, PathComparison())
                    || fullPath.StartsWith(storagePath + "-", PathComparison()))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(
                    config.KnowledgeBase.Root,
                    fullPath).Replace('\\', '/');
                if (normalizedPrefix is not null
                    && !relativePath.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
                    && !relativePath.StartsWith(
                        normalizedPrefix + "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!matcher.Match(relativePath).HasMatches) continue;
                files.Add(new WorkspaceFile(
                    relativePath,
                    info.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                        ? WorkspaceFileKind.Markdown
                        : WorkspaceFileKind.Asset,
                    info.Length,
                    info.LastWriteTimeUtc));
            }
        }

        return Task.FromResult(new WorkspaceFileList(files
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList()));
    }

    public async Task<MarkdownOutline> GetOutlineAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var normalized = pathGuard.ValidateRelativePath(path);
        _ = pathGuard.ResolveMarkdownPath(normalized);
        var document = await loader.LoadExistingAsync(
            config.KnowledgeBase,
            normalized,
            cancellationToken);
        var roots = new List<MutableOutlineNode>();
        var stack = new Stack<MutableOutlineNode>();
        foreach (var heading in parser.Parse(document)
                     .Where(element => element.Kind == MarkdownElementKind.Heading))
        {
            var node = new MutableOutlineNode(
                SemanticAnchor.FromElement(heading).ToString(),
                heading.HeadingLevel,
                HeadingTextRegex().Replace(
                    heading.Text.Split(['\r', '\n'], 2)[0],
                    "$1").Trim());
            while (stack.Count > 0 && stack.Peek().Level >= node.Level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack.Peek().Children.Add(node);
            }

            stack.Push(node);
        }

        return new MarkdownOutline(
            normalized,
            document.SourceHash,
            roots.Select(ToContract).ToList());
    }

    private static OutlineNode ToContract(MutableOutlineNode node)
        => new(
            node.Pointer,
            node.Level,
            node.Title,
            node.Children.Select(ToContract).ToList());

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    [GeneratedRegex(@"^#{1,6}\s+(.+?)(?:\s+#+\s*)?$")]
    private static partial Regex HeadingTextRegex();

    private sealed record MutableOutlineNode(string Pointer, int Level, string Title)
    {
        public List<MutableOutlineNode> Children { get; } = [];
    }
}
