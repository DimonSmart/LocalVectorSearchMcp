namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;

internal sealed record CommandLineConfigOptions
{
    public string? ConfigPath { get; init; }
    public string? Root { get; init; }
    public string? StoragePath { get; init; }
    public string? EmbeddingEndpoint { get; init; }
    public string? EmbeddingModel { get; init; }
    public IReadOnlyList<string> Include { get; init; } = [];
    public IReadOnlyList<string> Exclude { get; init; } = [];
}
