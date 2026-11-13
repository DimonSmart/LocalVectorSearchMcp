namespace DimonSmart.LocalVectorSearchMcp.Core.Configuration;

public sealed record PathsConfig
{
    public string RelativeTo { get; init; } = "project";
}
