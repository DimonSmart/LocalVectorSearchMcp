using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;

public sealed class WorkspaceIndexSynchronizer(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard,
    IMarkdownDocumentLoader loader,
    IMarkdownElementParser parser,
    IMarkdownChunker chunker,
    IEmbeddingProvider embeddingProvider,
    IIndexInitializer initializer,
    IIndexManifestService manifest,
    IDocumentIndexStore store,
    IIndexSynchronizationState state,
    IndexOperationGate? operationGate = null) : IWorkspaceIndexSynchronizer
{
    private readonly IndexOperationGate gate = operationGate ?? new IndexOperationGate();

    public Task<bool> ReconcileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var normalized = pathGuard.ValidateRelativePath(relativePath);
        return gate.RunAsync(async () =>
        {
            try
            {
                await EnsureCompatibleIndexAsync(cancellationToken);
                var absolutePath = pathGuard.ResolveMarkdownPath(normalized);
                if (!File.Exists(absolutePath))
                {
                    var deleted = await store.DeleteDocumentAsync(normalized, cancellationToken);
                    state.MarkSynchronized(normalized);
                    return deleted;
                }

                var document = await loader.LoadFileAsync(config.KnowledgeBase, normalized, cancellationToken);
                var hashes = await store.GetDocumentHashesAsync(cancellationToken);
                if (hashes.TryGetValue(normalized, out var indexedHash)
                    && indexedHash == document.SourceHash)
                {
                    state.MarkSynchronized(normalized);
                    return false;
                }

                var elements = parser.Parse(document);
                var chunks = chunker.BuildChunks(document, elements);
                var vectors = new List<EmbeddingVector>();
                foreach (var batch in chunks.Chunk(config.Embedding.BatchSize))
                {
                    vectors.AddRange(await embeddingProvider.EmbedBatchAsync(
                        batch.Select(chunk => chunk.EmbeddingText).ToList(),
                        cancellationToken));
                }

                await store.SaveDocumentIndexAsync(document, elements, chunks, vectors, cancellationToken);
                state.MarkSynchronized(normalized);
                return true;
            }
            catch (Exception exception)
            {
                state.MarkDirty(normalized, exception.Message);
                throw;
            }
        }, cancellationToken);
    }

    private async Task EnsureCompatibleIndexAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
        if (!await manifest.HasManifestAsync(cancellationToken))
        {
            await manifest.WriteCurrentManifestAsync(cancellationToken);
            return;
        }

        var compatibility = await manifest.CheckCompatibilityAsync(cancellationToken);
        if (!compatibility.IsCompatible)
        {
            throw new IndexCompatibilityException(
                "Index is incompatible with current configuration. Run kb_reindex with force=true.");
        }
    }
}
