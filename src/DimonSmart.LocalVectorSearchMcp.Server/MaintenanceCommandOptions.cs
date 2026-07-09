namespace DimonSmart.LocalVectorSearchMcp.Server;

internal sealed record MaintenanceCommandOptions(
    bool Reindex,
    bool Status,
    bool Force)
{
    public bool IsMaintenanceCommand => Reindex || Status;

    public static MaintenanceCommandOptions Parse(string[] args)
        => new(
            Reindex: args.Contains("--reindex", StringComparer.Ordinal),
            Status: args.Contains("--status", StringComparer.Ordinal),
            Force: args.Contains("--force", StringComparer.Ordinal));
}
