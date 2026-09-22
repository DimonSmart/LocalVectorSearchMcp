using System.ComponentModel;
using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[McpServerToolType]
public sealed class KnowledgeMcpTools(
    IReindexCoordinator reindexCoordinator,
    IIndexInitializer indexInitializer,
    IIndexStatusReader statusReader,
    IKnowledgeSearchService searchService,
    ISemanticPointerReader reader,
    IWorkspaceMutationService mutations,
    IWorkspaceNavigationService navigation)
{
    [McpServerTool(Name = "kb_reindex")]
    [Description("Starts reindexing of the configured Markdown root in the background. Only one reindex can run at a time. Use kb_status to monitor progress and obtain the final result.")]
    public Task<ReindexStartResponse> ReindexAsync(
        ReindexToolRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            reindexCoordinator.TryStart(
                new ReindexRequest(request.Scope, request.Force)));
    }

    [McpServerTool(Name = "kb_status")]
    [Description("Returns local vector search index status.")]
    public async Task<StatusResponse> StatusAsync(CancellationToken cancellationToken)
    {
        await indexInitializer.InitializeAsync(cancellationToken);
        return await statusReader.GetStatusAsync(cancellationToken);
    }

    [McpServerTool(Name = "kb_search", UseStructuredContent = true, OutputSchemaType = typeof(SearchResponse))]
    [Description("Searches the derived Markdown index using lexical, semantic or hybrid search. Results belong to an indexed revision identified by indexedSourceHash and may temporarily lag behind the current source. Hybrid search falls back to lexical search when the embedding provider is unavailable.")]
    public async Task<CallToolResult> SearchAsync(
        SearchToolRequest request,
        CancellationToken cancellationToken)
    {
        int? topK = request.TopK is null
            ? null
            : Math.Clamp(request.TopK.Value, 1, 50);
        try
        {
            if (IsDestructiveRebuildRunning())
            {
                return IndexRebuildInProgressError();
            }

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
        catch (EmbeddingProviderException exception)
        {
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text =
                            $"Semantic search is unavailable: {exception.Message} " +
                            "Use mode=\"lexical\"; hybrid search falls back to lexical automatically."
                    }
                ],
                IsError = true
            };
        }
    }

    [McpServerTool(
        Name = "kb_read",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MarkdownSlice))]
    [Description("Reads the current Markdown source starting at a semantic pointer or hashed semantic anchor. Reading is independent of embeddings and background indexing. Public concrete pointers are returned as logical~selfHash~subtreeHash; legacy logical~selfHash input remains valid for navigation. Omit pointer or use \"document\" to read from the beginning of the document.")]
    public async Task<CallToolResult> ReadAsync(
        ReadToolRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var anchor = SemanticAnchorParser.Parse(request.Pointer ?? "document");
            var response = await reader.ReadAsync(
                request.Path,
                anchor,
                request.MaxElements ?? 20,
                request.MaxBytes ?? 12000,
                cancellationToken);
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = System.Text.Json.JsonSerializer.Serialize(
                            response,
                            JsonOptions.Default)
                    }
                ],
                StructuredContent = System.Text.Json.JsonSerializer.SerializeToElement(
                    response,
                    JsonOptions.Default)
            };
        }
        catch (Exception exception) when (IsControlledToolException(exception))
        {
            return ControlledToolError(exception);
        }
    }

    [McpServerTool(
        Name = "kb_patch",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MutationResponse))]
    [Description("Atomically edits Markdown using hashed semantic anchors, commits the source file, and schedules index reconciliation. Public concrete pointers use logical~selfHash~subtreeHash with 16-character lowercase hashes. replace_element, insert_before, insert_after, and delete validate only selfHash, so an unchanged element can survive independent descendant edits. replace is a deprecated alias of replace_element. replace_section replaces a heading and all source in its section through the next heading with level <= the target level or end of document; use it when rewriting an entire chapter or section. It requires canonical v2 input and additionally validates subtreeHash so stale body or raw-content edits cannot be overwritten. Relocation uses only exact element kind plus selfHash. Legacy logical~selfHash input remains accepted for Self operations but is rejected for replace_section with a reread instruction. The document pointer remains unhashed and supports insert_before and insert_after at document boundaries. IndexSynchronized reports whether derived-index synchronization has already been confirmed.")]
    public Task<CallToolResult> PatchAsync(
        PatchToolRequest request,
        CancellationToken cancellationToken)
        => RunMutationAsync(() => mutations.PatchAsync(
            new PatchRequest(
                request.Path,
                request.Operations.Select(operation => new PatchOperation(
                    operation.Kind,
                    operation.Pointer,
                    operation.Markdown)).ToList()),
            cancellationToken));

    [McpServerTool(
        Name = "kb_create",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MutationResponse))]
    [Description("Creates a new UTF-8 Markdown file, commits it as the source of truth, and schedules index synchronization. IndexSynchronized reports whether derived-index synchronization has already been confirmed.")]
    public Task<CallToolResult> CreateAsync(
        CreateToolRequest request,
        CancellationToken cancellationToken)
        => RunMutationAsync(() => mutations.CreateAsync(
            request.Path,
            request.Markdown,
            cancellationToken));

    [McpServerTool(
        Name = "kb_move",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MutationResponse))]
    [Description("Moves a Markdown file when its source revision matches, commits the filesystem move, and schedules index reconciliation for the affected paths. IndexSynchronized reports whether derived-index synchronization has already been confirmed.")]
    public Task<CallToolResult> MoveAsync(
        MoveToolRequest request,
        CancellationToken cancellationToken)
        => RunMutationAsync(() => mutations.MoveAsync(
            new MoveRequest(
                request.SourcePath,
                request.TargetPath,
                request.ExpectedSourceHash),
            cancellationToken));

    [McpServerTool(
        Name = "kb_delete",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MutationResponse))]
    [Description("Deletes a Markdown file when its source revision matches, commits the source deletion, and schedules removal from the derived index. IndexSynchronized reports whether derived-index synchronization has already been confirmed.")]
    public Task<CallToolResult> DeleteAsync(
        DeleteToolRequest request,
        CancellationToken cancellationToken)
        => RunMutationAsync(() => mutations.DeleteAsync(
            new DeleteRequest(
                request.Path,
                request.ExpectedSourceHash),
            cancellationToken));

    private static async Task<CallToolResult> RunMutationAsync(
        Func<Task<MutationResponse>> mutation)
    {
        try
        {
            var response = await mutation();
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = System.Text.Json.JsonSerializer.Serialize(
                            response,
                            JsonOptions.Default)
                    }
                ],
                StructuredContent = System.Text.Json.JsonSerializer.SerializeToElement(
                    response,
                    JsonOptions.Default)
            };
        }
        catch (Exception exception) when (IsControlledToolException(exception))
        {
            return ControlledToolError(exception);
        }
    }

    private static bool IsControlledToolException(Exception exception)
        => exception is WorkspaceMutationException
            or DocumentNotFoundException
            or DocumentConflictException
            or SemanticAnchorConflictException
            or SemanticPointerFormatException
            or SemanticPointerNotFoundException
            or KnowledgeBaseAccessException;

    private static CallToolResult ControlledToolError(Exception exception)
        => exception switch
        {
            DocumentNotFoundException documentNotFound =>
                throw new McpProtocolException(
                    documentNotFound.Message,
                    documentNotFound,
                    McpErrorCode.ResourceNotFound),
            _ => new CallToolResult
            {
                Content = [new TextContentBlock { Text = exception.Message }],
                IsError = true
            }
        };

    private bool IsDestructiveRebuildRunning()
        => reindexCoordinator?.GetStatus().Current?.IsDestructiveRebuild == true;

    private static CallToolResult IndexRebuildInProgressError()
        => new()
        {
            Content =
            [
                new TextContentBlock
                {
                    Text =
                        "Index rebuild is currently in progress. " +
                        "Retry after kb_status reports indexing.isRunning = false."
                }
            ],
            IsError = true
        };

    [McpServerTool(Name = "kb_list_files")]
    [Description("Lists Markdown and asset files under the configured workspace root.")]
    public Task<WorkspaceFileList> ListFilesAsync(
        ListFilesToolRequest request,
        CancellationToken cancellationToken)
        => navigation.ListFilesAsync(request.PathPrefix, request.IncludeGlob, cancellationToken);

    [McpServerTool(Name = "kb_outline")]
    [Description("Returns a deterministic heading outline for one Markdown file.")]
    public async Task<CallToolResult> OutlineAsync(
        OutlineToolRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await navigation.GetOutlineAsync(
                request.Path,
                cancellationToken);
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = System.Text.Json.JsonSerializer.Serialize(
                            response,
                            JsonOptions.Default)
                    }
                ]
            };
        }
        catch (Exception exception) when (IsControlledToolException(exception))
        {
            return ControlledToolError(exception);
        }
    }
}
