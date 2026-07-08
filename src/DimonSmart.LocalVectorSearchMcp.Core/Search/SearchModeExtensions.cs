namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public static class SearchModeExtensions
{
    public static string ToWireValue(this SearchMode mode) => mode switch
    {
        SearchMode.Semantic => "semantic",
        SearchMode.Lexical => "lexical",
        SearchMode.Hybrid => "hybrid",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown search mode.")
    };

    public static bool TryParseWireValue(string? value, out SearchMode mode)
    {
        if (string.Equals(value, "semantic", StringComparison.OrdinalIgnoreCase))
        {
            mode = SearchMode.Semantic;
            return true;
        }

        if (string.Equals(value, "lexical", StringComparison.OrdinalIgnoreCase))
        {
            mode = SearchMode.Lexical;
            return true;
        }

        if (string.Equals(value, "hybrid", StringComparison.OrdinalIgnoreCase))
        {
            mode = SearchMode.Hybrid;
            return true;
        }

        mode = default;
        return false;
    }
}
