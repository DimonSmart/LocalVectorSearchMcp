namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record MarkdownSlice(string KnowledgeBase, string Path, string Pointer, IReadOnlyList<MarkdownSliceElement> Elements, string Markdown, string? NextPointer);
