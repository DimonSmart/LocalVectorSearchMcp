using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public sealed record MarkdownElement(
    string Path,
    SemanticPointer Pointer,
    MarkdownElementKind Kind,
    string Text,
    int StartLine,
    int EndLine,
    int HeadingLevel,
    string? HeadingPath);
