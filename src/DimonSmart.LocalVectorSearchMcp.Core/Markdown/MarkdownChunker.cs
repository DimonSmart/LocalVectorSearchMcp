using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using System.Text;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public sealed class MarkdownChunker(ChunkingConfig config, EmbeddingTextBuilder textBuilder) : IMarkdownChunker
{
    public const string Version = "1";

    public IReadOnlyList<MarkdownChunk> BuildChunks(MarkdownSourceDocument document, IReadOnlyList<MarkdownElement> elements)
    {
        var indexable = elements.Where(e => e.Kind != MarkdownElementKind.Document && e.Kind != MarkdownElementKind.Heading && (config.IncludeFrontMatter || e.Kind != MarkdownElementKind.FrontMatter)).ToList();
        var chunks = new List<MarkdownChunk>();
        var current = new List<MarkdownElement>();

        foreach (var element in indexable)
        {
            var candidate = current.Concat([element]).ToList();
            if (current.Count > 0 && (candidate.Count > config.MaxElements || Encoding.UTF8.GetByteCount(BuildText(candidate)) > config.MaxChunkBytes))
            {
                chunks.Add(CreateChunk(document, current));
                current.Clear();
            }

            current.Add(element);
        }

        if (current.Count > 0)
        {
            chunks.Add(CreateChunk(document, current));
        }

        return chunks;
    }

    private MarkdownChunk CreateChunk(MarkdownSourceDocument document, IReadOnlyList<MarkdownElement> elements)
    {
        var text = BuildText(elements);
        var headingPath = elements.LastOrDefault(e => e.HeadingPath is not null)?.HeadingPath;
        var embeddingText = textBuilder.Build(document.RelativePath, headingPath, text);
        return new MarkdownChunk(document.RelativePath, elements[0].Pointer, elements, text, headingPath, embeddingText, StableHash.HashText(embeddingText));
    }

    private static string BuildText(IEnumerable<MarkdownElement> elements) => string.Join("\n\n", elements.Select(e => e.Text.Trim()));
}
