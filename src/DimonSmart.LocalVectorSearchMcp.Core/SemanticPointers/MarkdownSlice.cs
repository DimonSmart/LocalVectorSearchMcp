namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed record MarkdownSlice(string Path, string Pointer, IReadOnlyList<MarkdownSliceElement> Elements, string Markdown, string? NextPointer);
