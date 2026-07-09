using System.ComponentModel;
using DimonSmart.LocalVectorSearchMcp.Core;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

[McpServerToolType]
public sealed class KnowledgeMcpTools(
    IKnowledgeBaseIndexer indexer,
    IIndexInitializer indexInitializer,
    IIndexStatusReader statusReader,
    IKnowledgeSearchService searchService,
    ISemanticPointerReader reader)
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
            new SearchRequest(request.Query, request.Mode, topK),
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
}
