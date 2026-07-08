namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record ChunkingConfig
{
    public int MaxChunkBytes { get; init; } = 4096;
    public int MaxElements { get; init; } = 20;
    public bool IncludeHeadingContext { get; init; } = true;
    public bool IncludeFrontMatter { get; init; } = true;
}
