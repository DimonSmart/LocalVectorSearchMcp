using System.ComponentModel;
using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using ModelContextProtocol.Protocol;
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

    [McpServerTool(Name = "kb_search", UseStructuredContent = true, OutputSchemaType = typeof(SearchResponse))]
    [Description("Searches the local Markdown knowledge base using lexical, semantic or hybrid search.")]
    public async Task<CallToolResult> SearchAsync(
        SearchToolRequest request,
        CancellationToken cancellationToken)
    {
        int? topK = request.TopK is null
            ? null
            : Math.Clamp(request.TopK.Value, 1, 50);
        try
        {
            await indexInitializer.InitializeAsync(cancellationToken);
            var response = await searchService.SearchAsync(
                new SearchRequest(request.Query, request.Mode, topK, request.IncludeGlobs, request.ExcludeGlobs),
                cancellationToken);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = System.Text.Json.JsonSerializer.Serialize(response, JsonOptions.Default) }],
                StructuredContent = System.Text.Json.JsonSerializer.SerializeToElement(response, JsonOptions.Default)
            };
        }
        catch (IndexNotReadyException exception)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = exception.Message }],
                IsError = true
            };
        }
    }

    [McpServerTool(Name = "kb_read")]
    [Description("Reads indexed Markdown content starting at a semantic pointer. Omit pointer or use \"document\" to read from the beginning of the document.")]
    public Task<MarkdownSlice> ReadAsync(
        ReadToolRequest request,
        CancellationToken cancellationToken)
    {
        var pointer = SemanticPointerParser.Parse(request.Pointer ?? "document");
        return reader.ReadAsync(
            request.Path,
            pointer,
            request.MaxElements ?? 20,
            request.MaxBytes ?? 12000,
            cancellationToken);
    }

    [McpServerTool(Name = "kb_patch", UseStructuredContent = true, OutputSchemaType = typeof(MutationResponse))]
    [Description("Atomically edits Markdown using semantic pointers and the current sourceHash. Pass operations as an array with kind replace, insert_before, insert_after, or delete. Each pointer addresses a semantic element. Replace and insert operations require markdown; delete does not. Use expectedSourceHash from the latest read. The document pointer supports insert_before and insert_after at document boundaries.")]
    public async Task<CallToolResult> PatchAsync(
        PatchToolRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await mutations.PatchAsync(
                new PatchRequest(
                    request.Path,
                    request.ExpectedSourceHash,
                    request.Operations.Select(operation => new PatchOperation(
                        operation.Kind,
                        operation.Pointer,
                        operation.Markdown)).ToList()),
                cancellationToken);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = System.Text.Json.JsonSerializer.Serialize(response, JsonOptions.Default) }],
                StructuredContent = System.Text.Json.JsonSerializer.SerializeToElement(response, JsonOptions.Default)
            };
        }
        catch (Exception exception) when (exception is WorkspaceMutationException
                                          or DocumentConflictException
                                          or KnowledgeBaseAccessException)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = exception.Message }],
                IsError = true
            };
        }
    }

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
}
