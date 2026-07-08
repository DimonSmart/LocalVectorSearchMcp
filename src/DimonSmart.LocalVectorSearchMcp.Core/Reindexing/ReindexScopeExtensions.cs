namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

public static class ReindexScopeExtensions
{
    public static string ToWireValue(this ReindexScope scope) => scope switch
    {
        ReindexScope.Changed => "changed",
        ReindexScope.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown reindex scope.")
    };

    public static bool TryParseWireValue(string? value, out ReindexScope scope)
    {
        if (string.Equals(value, "changed", StringComparison.OrdinalIgnoreCase))
        {
            scope = ReindexScope.Changed;
            return true;
        }

        if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            scope = ReindexScope.All;
            return true;
        }

        scope = default;
        return false;
    }
}
