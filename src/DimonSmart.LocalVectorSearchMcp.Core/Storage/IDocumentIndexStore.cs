using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Core.Storage;

public interface IDocumentIndexStore
{
    Task<IReadOnlyDictionary<string, string>> GetDocumentHashesAsync(CancellationToken cancellationToken);
    Task SaveDocumentIndexAsync(
        MarkdownSourceDocument document,
        IReadOnlyList<MarkdownElement> elements,
        IReadOnlyList<MarkdownChunk> chunks,
        IReadOnlyList<EmbeddingVector> vectors,
        CancellationToken cancellationToken);
    Task<int> DeleteMissingDocumentsAsync(
        IReadOnlySet<string> currentRelativePaths,
        CancellationToken cancellationToken);
}
