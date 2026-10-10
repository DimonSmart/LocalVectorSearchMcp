using System.Text.RegularExpressions;

namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

/// <summary>Physical list marker, independent of item ordinal and semantic depth.</summary>
public readonly record struct MarkdownListMarker(
    string Indentation, string MarkerStyle, string RawMarker)
{
    private static readonly Regex MarkerPattern = new(
        @"^(?<indent>[ \t]*)(?<marker>[-+*]|[0-9]+[.)])(?=[ \t]|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string firstLine, out MarkdownListMarker marker)
    {
        var match = MarkerPattern.Match(firstLine);
        if (!match.Success)
        {
            marker = default;
            return false;
        }

        var raw = match.Groups["marker"].Value;
        marker = new MarkdownListMarker(match.Groups["indent"].Value,
            char.IsDigit(raw[0]) ? "ordered:" + raw[^1] : raw, raw);
        return true;
    }
}
