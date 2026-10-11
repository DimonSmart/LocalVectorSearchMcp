using System.ComponentModel;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceFileMcpTools(
    IWorkspaceFileDownloadService files)
{
    [McpServerTool(Name = "kb_download_file")]
    [Description("Returns one permitted root-relative workspace file as an embedded binary resource, followed by JSON metadata. Accepts any regular file up to 25 MiB. Git metadata, internal index files, absolute/traversal paths, and paths through reparse points are not permitted. Does not require writes and does not modify the workspace or index.")]
    public async Task<CallToolResult> DownloadFileAsync(
        [Description("Root-relative workspace file path.")]
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await files.DownloadAsync(path, cancellationToken);
            return new CallToolResult
            {
                Content =
                [
                    new EmbeddedResourceBlock
                    {
                        Resource = BlobResourceContents.FromBytes(
                            file.Data,
                            ResourceUri(file.Path),
                            file.MimeType)
                    },
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(new
                        {
                            file.Path,
                            file.MimeType,
                            file.Bytes,
                            file.Sha256
                        }, JsonOptions.Default)
                    }
                ]
            };
        }
        catch (Exception exception) when (
            exception is WorkspaceFileNotFoundException
                or WorkspaceMutationException
                or KnowledgeBaseAccessException
                or UnauthorizedAccessException
                or IOException)
        {
            return ToolErrors.FromException(exception);
        }
    }

    private static string ResourceUri(string path)
        => "resource://workspace/" + string.Join(
            '/',
            path.Split('/').Select(Uri.EscapeDataString));
}
