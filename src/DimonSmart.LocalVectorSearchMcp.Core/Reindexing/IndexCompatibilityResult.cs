namespace DimonSmart.LocalVectorSearchMcp.Core.Reindexing;

public sealed record IndexCompatibilityResult(
    bool IsCompatible,
    IReadOnlyList<string> Problems);
