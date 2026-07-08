using DimonSmart.LocalVectorSearchMcp.Core.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed record MarkdownSliceElement(string Pointer, MarkdownElementKind Kind, string Text, string? HeadingPath);
