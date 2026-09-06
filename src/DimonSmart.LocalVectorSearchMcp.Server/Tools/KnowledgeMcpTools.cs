using System.ComponentModel;
using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[McpServerToolType]
public sealed class KnowledgeMcpTools(
    IKnowledgeBaseIndexer indexer,
    IIndexInitializer indexInitializer,
    IIndexStatusReader statusReader,
    IKnowledgeSearchService searchService,
    ISemanticPointerReader reader,
    IWorkspaceMutationService mutations,
    IWorkspaceNavigationService navigation)
{
    [McpServerTool(Name = "kb_reindex")]
    [Description("Indexes or reindexes the current project's configured Markdown root.")]
    public Task<ReindexResponse> ReindexAsync(
        ReindexToolRequest request,
        CancellationToken cancellationToken)
        => indexer.ReindexAsync(
            new ReindexRequest(request.Scope, request.Force),
            cancellationToken);

    [McpServerTool(Name = "kb_status")]
    [Description("Returns local vector search index status.")]
    public async Task<StatusResponse> StatusAsync(CancellationToken cancellationToken)
    {
        await indexInitializer.InitializeAsync(cancellationToken);
        return await statusReader.GetStatusAsync(cancellationToken);
    }

    [McpServerTool(Name = "kb_search")]
    [Description("Searches the local Markdown knowledge base using lexical, semantic or hybrid search.")]
    public Task<SearchResponse> SearchAsync(
        SearchToolRequest request,
        CancellationToken cancellationToken)
    {
        int? topK = request.TopK is null
            ? null
            : Math.Clamp(request.TopK.Value, 1, 50);
        return searchService.SearchAsync(
            new SearchRequest(request.Query, request.Mode, topK, request.IncludeGlobs, request.ExcludeGlobs),
            cancellationToken);
    }

    [McpServerTool(Name = "kb_read")]
    [Description("Reads indexed Markdown content from a document starting at a semantic pointer.")]
    public Task<MarkdownSlice> ReadAsync(
        ReadToolRequest request,
        CancellationToken cancellationToken)
    {
        var pointer = SemanticPointerParser.Parse(request.Pointer);
        return reader.ReadAsync(
            request.Path,
            pointer,
            request.MaxElements ?? 20,
            request.MaxBytes ?? 12000,
            cancellationToken);
    }

    [McpServerTool(Name = "kb_patch")]
    [Description("Atomically edits Markdown elements by semantic pointer when the source revision matches.")]
    public Task<MutationResponse> PatchAsync(
        PatchToolRequest request,
        CancellationToken cancellationToken)
        => mutations.PatchAsync(
            new PatchRequest(
                request.Path,
                request.ExpectedSourceHash,
                request.Operations.Select(operation => new PatchOperation(
                    ParsePatchKind(operation.Kind),
                    operation.Pointer,
                    operation.Markdown)).ToList()),
            cancellationToken);

    [McpServerTool(Name = "kb_create")]
    [Description("Creates a new UTF-8 Markdown file and synchronizes it with the index.")]
    public Task<MutationResponse> CreateAsync(
        CreateToolRequest request,
        CancellationToken cancellationToken)
        => mutations.CreateAsync(request.Path, request.Markdown, cancellationToken);

    [McpServerTool(Name = "kb_move")]
    [Description("Moves a Markdown file when its source revision matches and synchronizes the index.")]
    public Task<MutationResponse> MoveAsync(
        MoveToolRequest request,
        CancellationToken cancellationToken)
        => mutations.MoveAsync(
            new MoveRequest(request.SourcePath, request.TargetPath, request.ExpectedSourceHash),
            cancellationToken);

    [McpServerTool(Name = "kb_delete")]
    [Description("Deletes a Markdown file when its source revision matches and removes it from the index.")]
    public Task<MutationResponse> DeleteAsync(
        DeleteToolRequest request,
        CancellationToken cancellationToken)
        => mutations.DeleteAsync(
            new DeleteRequest(request.Path, request.ExpectedSourceHash),
            cancellationToken);

    [McpServerTool(Name = "kb_list_files")]
    [Description("Lists Markdown and asset files under the configured workspace root.")]
    public Task<WorkspaceFileList> ListFilesAsync(
        ListFilesToolRequest request,
        CancellationToken cancellationToken)
        => navigation.ListFilesAsync(request.PathPrefix, request.IncludeGlob, cancellationToken);

    [McpServerTool(Name = "kb_outline")]
    [Description("Returns a deterministic heading outline for one Markdown file.")]
    public Task<MarkdownOutline> OutlineAsync(
        OutlineToolRequest request,
        CancellationToken cancellationToken)
        => navigation.GetOutlineAsync(request.Path, cancellationToken);

    private static PatchOperationKind ParsePatchKind(string kind)
        => kind.Trim().ToLowerInvariant() switch
        {
            "replace" => PatchOperationKind.Replace,
            "insert_before" => PatchOperationKind.InsertBefore,
            "insert_after" => PatchOperationKind.InsertAfter,
            "delete" => PatchOperationKind.Delete,
            _ => throw new WorkspaceMutationException(
                "Patch operation kind must be replace, insert_before, insert_after, or delete.")
        };
}
