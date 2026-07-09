using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;

public interface IKnowledgeRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> GetDocumentHashesAsync(CancellationToken cancellationToken);
    Task SaveDocumentIndexAsync(MarkdownSourceDocument document, IReadOnlyList<MarkdownElement> elements, IReadOnlyList<MarkdownChunk> chunks, IReadOnlyList<EmbeddingVector> vectors, CancellationToken cancellationToken);
    Task<int> DeleteMissingDocumentsAsync(IReadOnlySet<string> currentRelativePaths, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChunkSearchDocument>> GetChunksAsync(IReadOnlyCollection<long> chunkIds, CancellationToken cancellationToken);
    Task<MarkdownSlice> ReadSliceAsync(string path, SemanticPointer pointer, int maxElements, int maxBytes, CancellationToken cancellationToken);
    Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken);
    Task<bool> HasChunksAsync(CancellationToken cancellationToken);
}
