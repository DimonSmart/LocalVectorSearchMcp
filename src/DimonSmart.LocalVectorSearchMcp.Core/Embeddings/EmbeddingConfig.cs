namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record EmbeddingConfig
{
    public string Provider { get; init; } = "openai-compatible";
    public string Endpoint { get; init; } = "http://localhost:11434/v1";
    public string ApiKey { get; init; } = "ollama";
    public string Model { get; init; } = "bge-m3:latest";
    public int? Dimensions { get; init; }
    public int BatchSize { get; init; } = 16;
    public bool AllowRemoteEndpoint { get; init; }
    public int TimeoutSeconds { get; init; } = 120;
}
