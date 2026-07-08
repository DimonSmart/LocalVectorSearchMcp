namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public static class HybridRanker
{
    public static IReadOnlyList<(long ChunkId, double Score)> Fuse(IEnumerable<long> semanticIds, IEnumerable<long> lexicalIds, int rrfK, int topK)
    {
        var scores = new Dictionary<long, double>();
        Add(semanticIds);
        Add(lexicalIds);
        return scores.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(topK).Select(x => (x.Key, x.Value)).ToList();

        void Add(IEnumerable<long> ids)
        {
            var rank = 1;
            foreach (var id in ids)
            {
                scores[id] = scores.GetValueOrDefault(id) + 1d / (rrfK + rank);
                rank++;
            }
        }
    }
}
