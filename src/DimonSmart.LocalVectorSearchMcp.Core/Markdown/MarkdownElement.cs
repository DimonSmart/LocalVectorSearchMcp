namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record MarkdownElement(
    string KnowledgeBase,
    string Path,
    SemanticPointer Pointer,
    MarkdownElementKind Kind,
    string Text,
    int StartLine,
    int EndLine,
    int HeadingLevel,
    string? HeadingPath);
