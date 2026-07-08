using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;

public sealed class KnowledgeBaseIndexer(LocalVectorSearchMcpConfig config, IMarkdownDocumentLoader loader, IMarkdownElementParser parser, IMarkdownChunker chunker, IEmbeddingProvider embeddingProvider, IKnowledgeRepository repository) : IKnowledgeBaseIndexer
{
    public async Task<ReindexResponse> ReindexAsync(ReindexRequest request, CancellationToken cancellationToken)
    {
        await repository.InitializeAsync(cancellationToken);
        var bases = config.KnowledgeBases.Where(kb => request.KnowledgeBase is null || kb.Name == request.KnowledgeBase).ToList();
        if (bases.Count == 0) throw new ConfigurationException($"Knowledge base was not found: {request.KnowledgeBase}");

        var scanned = 0;
        var indexed = 0;
        var skipped = 0;
        var deleted = 0;
        var chunksIndexed = 0;
        foreach (var kb in bases)
        {
            var documents = await loader.LoadAsync(kb, cancellationToken);
            scanned += documents.Count;
            var existing = await repository.GetDocumentHashesAsync(kb.Name, cancellationToken);
            deleted += await repository.DeleteMissingDocumentsAsync(kb.Name, documents.Select(d => d.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);

            foreach (var document in documents)
            {
                if (request.Scope == ReindexScope.Changed && existing.TryGetValue(document.RelativePath, out var hash) && hash == document.ContentHash)
                {
                    skipped++;
                    continue;
                }

                var elements = parser.Parse(document);
                var chunks = chunker.BuildChunks(document, elements);
                var vectors = new List<EmbeddingVector>();
                foreach (var batch in chunks.Chunk(config.Embedding.BatchSize))
                {
                    vectors.AddRange(await embeddingProvider.EmbedBatchAsync(batch.Select(c => c.EmbeddingText).ToList(), cancellationToken));
                }

                await repository.SaveDocumentIndexAsync(document, elements, chunks, vectors, cancellationToken);
                indexed++;
                chunksIndexed += chunks.Count;
            }
        }

        return new ReindexResponse(scanned, indexed, skipped, deleted, chunksIndexed, null);
    }
}
