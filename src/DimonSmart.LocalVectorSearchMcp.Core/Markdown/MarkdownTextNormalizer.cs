using System.Text.RegularExpressions;

namespace DimonSmart.LocalVectorSearchMcp.Core;

public static class MarkdownTextNormalizer
{
    public static string Normalize(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n').Select(line => line.TrimEnd()).ToList();
        var normalized = string.Join('\n', lines);
        return Regex.Replace(normalized, @"\n{3,}", "\n\n").TrimEnd() + "\n";
    }
}
