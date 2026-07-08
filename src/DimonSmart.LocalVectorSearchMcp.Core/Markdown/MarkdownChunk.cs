namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record MarkdownChunk(
    string KnowledgeBase,
    string Path,
    SemanticPointer StartPointer,
    IReadOnlyList<MarkdownElement> Elements,
    string Text,
    string? HeadingPath,
    string EmbeddingText,
    string EmbeddingTextHash);
