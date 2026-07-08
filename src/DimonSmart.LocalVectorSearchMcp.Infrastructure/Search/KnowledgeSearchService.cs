using DimonSmart.LocalVectorSearchMcp.Core;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure;

public sealed class KnowledgeSearchService(LocalVectorSearchMcpConfig config, IEmbeddingProvider embeddingProvider, IVectorIndexService vectorSearch, IFullTextSearchService fullTextSearch, IKnowledgeRepository repository) : IKnowledgeSearchService
{
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) throw new ConfigurationException("Query is required.");
        var mode = request.Mode ?? config.Search.DefaultMode;
        var topK = Math.Clamp(request.TopK ?? config.Search.MaxResults, 1, 50);
        if (!await repository.HasChunksAsync(cancellationToken)) throw new IndexNotReadyException("Index is empty. Run kb_reindex first.");

        var semantic = new List<SemanticSearchResult>();
        var lexical = new List<LexicalSearchResult>();
        if (mode is SearchMode.Semantic or SearchMode.Hybrid)
        {
            var embedding = (await embeddingProvider.EmbedBatchAsync([request.Query], cancellationToken)).Single();
            semantic.AddRange(await vectorSearch.SearchAsync(embedding, config.Search.SemanticCandidatePoolSize, request.KnowledgeBase, cancellationToken));
        }

        if (mode is SearchMode.Lexical or SearchMode.Hybrid)
        {
            lexical.AddRange(await fullTextSearch.SearchAsync(request.Query, config.Search.LexicalCandidatePoolSize, request.KnowledgeBase, cancellationToken));
        }

        var ordered = mode switch
        {
            SearchMode.Semantic => semantic.Take(topK).Select((x, i) => (x.ChunkId, Score: 1d / (i + 1))).ToList(),
            SearchMode.Lexical => lexical.Take(topK).Select((x, i) => (x.ChunkId, Score: 1d / (i + 1))).ToList(),
            _ => HybridRanker.Fuse(semantic.Select(x => x.ChunkId), lexical.Select(x => x.ChunkId), config.Search.RrfK, topK).ToList()
        };

        var chunks = (await repository.GetChunksAsync(ordered.Select(x => x.ChunkId).ToList(), cancellationToken)).ToDictionary(x => x.ChunkId);
        var snippets = lexical.ToDictionary(x => x.ChunkId, x => x.Snippet);
        var results = ordered.Where(x => chunks.ContainsKey(x.ChunkId)).Select(x =>
        {
            var c = chunks[x.ChunkId];
            var pointer = new SemanticPointer(c.Pointer);
            return new SearchResultItem(c.KnowledgeBase, c.Path, c.Pointer, new FullSemanticPointer(c.Path, pointer).ToString(), x.Score, mode, c.HeadingPath, snippets.GetValueOrDefault(c.ChunkId) ?? MakeSnippet(c.Text), new ReadHint(c.Path, c.Pointer, 20, 12000));
        }).ToList();
        return new SearchResponse(results);
    }

    private static string MakeSnippet(string text) => text.Length <= 240 ? text : text[..240] + "...";
}
