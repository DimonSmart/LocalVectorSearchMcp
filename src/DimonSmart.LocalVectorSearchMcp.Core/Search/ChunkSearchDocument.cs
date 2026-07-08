namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record ChunkSearchDocument(long ChunkId, string KnowledgeBase, string Path, string Pointer, string Text, string? HeadingPath);
