using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteIndexStatusReader(
    SqliteConnectionFactory factory,
    LocalVectorSearchMcpConfig config) : IIndexStatusReader
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
            config.Embedding.Model,
            EffectiveEmbeddingDimensions,
            project);
    }
}
