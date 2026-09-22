using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;

public sealed class KnowledgeSearchService(
    LocalVectorSearchMcpConfig config,
    IEmbeddingProvider embeddingProvider,
    IVectorIndexService vectorSearch,
    IFullTextSearchService fullTextSearch,
    ISearchIndexStateReader indexStateReader,
    IChunkSearchDocumentReader chunkReader) : IKnowledgeSearchService
{
    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ConfigurationException("Query is required.");
        }

        var mode = request.Mode ?? config.Search.DefaultMode;
        var topK = Math.Clamp(request.TopK ?? config.Search.MaxResults, 1, 50);
        SearchPathScope? scope = request.IncludeGlobs is null && request.ExcludeGlobs is null
            ? null
            : new SearchPathScope(request.IncludeGlobs ?? [], request.ExcludeGlobs ?? []);
        if (scope is not null)
        {
            _ = new PathScopeMatcher(scope);
        }

        if (!await indexStateReader.HasChunksAsync(cancellationToken))
        {
            throw new IndexNotReadyException("Index is empty. Run kb_reindex first.");
        }

        var semantic = new List<SemanticSearchResult>();
        var lexical = new List<LexicalSearchResult>();
        var effectiveMode = mode;
        string? warning = null;
        if (mode is SearchMode.Semantic or SearchMode.Hybrid)
        {
            try
            {
                var embedding = (await embeddingProvider.EmbedBatchAsync(
                    [request.Query],
                    cancellationToken)).Single();
                semantic.AddRange(await vectorSearch.SearchAsync(
                    embedding,
                    config.Search.SemanticCandidatePoolSize,
                    scope,
                    cancellationToken));
            }
            catch (EmbeddingProviderException exception) when (mode == SearchMode.Hybrid)
            {
                effectiveMode = SearchMode.Lexical;
                warning =
                    $"Semantic search is unavailable: {exception.Message} Falling back to lexical search.";
            }
        }

        if (effectiveMode is SearchMode.Lexical or SearchMode.Hybrid)
        {
            lexical.AddRange(await fullTextSearch.SearchAsync(
                request.Query,
                config.Search.LexicalCandidatePoolSize,
                scope,
                cancellationToken));
        }

        var ordered = effectiveMode switch
        {
            SearchMode.Semantic => semantic.Take(topK)
                .Select((x, i) => (x.ChunkId, Score: 1d / (i + 1)))
                .ToList(),
            SearchMode.Lexical => lexical.Take(topK)
                .Select((x, i) => (x.ChunkId, Score: 1d / (i + 1)))
                .ToList(),
            _ => HybridRanker.Fuse(
                    semantic.Select(x => x.ChunkId),
                    lexical.Select(x => x.ChunkId),
                    config.Search.RrfK,
                    topK)
                .ToList()
        };

        var chunks = (await chunkReader.GetChunksAsync(
            ordered.Select(x => x.ChunkId).ToList(),
            cancellationToken)).ToDictionary(x => x.ChunkId);
        var snippets = lexical.ToDictionary(x => x.ChunkId, x => x.Snippet);
        var results = ordered.Where(x => chunks.ContainsKey(x.ChunkId)).Select(x =>
        {
            var chunk = chunks[x.ChunkId];
            if (chunk.ElementSelfHash is null || chunk.ElementSubtreeHash is null)
            {
                throw new InvalidOperationException(
                    $"Search chunk '{chunk.ChunkId}' has no semantic anchor hashes.");
            }

            var anchor = new SemanticAnchor(
                new SemanticPointer(chunk.Pointer),
                chunk.ElementSelfHash,
                chunk.ElementSubtreeHash).ToString();
            return new SearchResultItem(
                chunk.Path,
                anchor,
                $"{chunk.Path}::{anchor}",
                x.Score,
                effectiveMode,
                chunk.HeadingPath,
                snippets.GetValueOrDefault(chunk.ChunkId) ?? MakeSnippet(chunk.Text),
                new ReadHint(chunk.Path, anchor, 20, 12000));
        }).ToList();

        return new SearchResponse(results, warning);
    }

    private static string MakeSnippet(string text)
        => text.Length <= 240 ? text : text[..240] + "...";
}
