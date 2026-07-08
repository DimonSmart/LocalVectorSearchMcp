namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record SearchResultItem(string KnowledgeBase, string Path, string Pointer, string FullPointer, double Score, SearchMode SearchMode, string? HeadingPath, string Snippet, ReadHint ReadHint);
