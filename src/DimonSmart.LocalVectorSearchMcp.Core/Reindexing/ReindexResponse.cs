namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record ReindexResponse(int ScannedFiles, int IndexedFiles, int SkippedFiles, int DeletedFiles, int ChunksIndexed, string? Warning);
