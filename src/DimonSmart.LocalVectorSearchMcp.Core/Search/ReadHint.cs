namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record ReadHint(string Path, string Pointer, int MaxElements, int MaxBytes);
