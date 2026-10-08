using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteIndexStatusReader(
    SqliteConnectionFactory factory,
    LocalVectorSearchMcpConfig config,
    IIndexSynchronizationState? synchronizationState = null,
    IReindexStateReader? reindexStateReader = null,
    IIndexManifestService? manifestService = null) : IIndexStatusReader
{
    private int EffectiveEmbeddingDimensions => config.Embedding.Dimensions ?? 1024;

    public async Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        var documentCount = await db.ScalarLongAsync("select count(*) from documents", [], cancellationToken) ?? 0;
        var chunkCount = await db.ScalarLongAsync("select count(*) from chunks", [], cancellationToken) ?? 0;
        var lastIndexed = await db.ScalarStringAsync("select max(indexed_at_utc) from documents", [], cancellationToken);
        var project = new ProjectIndexStatus(
            config.KnowledgeBase.Root,
            (int)documentCount,
            (int)chunkCount,
            DateTimeOffset.TryParse(lastIndexed, out var value) ? value : null);
        return new StatusResponse(
            config.Storage.Path,
            SqliteSchema.Version,
            MarkdownChunker.Version,
            EmbeddingTextBuilder.Version,
            config.Embedding.Enabled ? config.Embedding.Model : "none",
            config.Embedding.Enabled ? EffectiveEmbeddingDimensions : null,
            project,
            synchronizationState?.GetStatus() ?? new IndexSynchronizationStatus(0, [], null),
            reindexStateReader?.GetStatus(),
            config.Embedding.Enabled ? "vector-enabled" : "lexical",
            config.Embedding.Provider,
            manifestService is not null && await manifestService.HasManifestAsync(cancellationToken)
                ? await manifestService.CheckCompatibilityAsync(cancellationToken)
                : null);
    }
}
