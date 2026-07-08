using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public sealed record MarkdownChunk(
    string KnowledgeBase,
    string Path,
    SemanticPointer StartPointer,
    IReadOnlyList<MarkdownElement> Elements,
    string Text,
    string? HeadingPath,
    string EmbeddingText,
    string EmbeddingTextHash);
