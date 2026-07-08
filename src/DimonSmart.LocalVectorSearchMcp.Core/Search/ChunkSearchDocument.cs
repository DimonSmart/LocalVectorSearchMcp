namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record ChunkSearchDocument(long ChunkId, string KnowledgeBase, string Path, string Pointer, string Text, string? HeadingPath);
