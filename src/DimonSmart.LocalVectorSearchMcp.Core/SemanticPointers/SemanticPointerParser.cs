using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using System.Text.RegularExpressions;

namespace DimonSmart.LocalVectorSearchMcp.Core;

public static partial class SemanticPointerParser
{
    public static SemanticPointer Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new FormatException($"Invalid semantic pointer: {value}");
        }

        return new SemanticPointer(value);
    }

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && PointerRegex().IsMatch(value);

    public static SemanticPointerKind GetKind(SemanticPointer pointer)
    {
        var value = pointer.Value;
        if (value == "frontmatter") return SemanticPointerKind.FrontMatter;
        if (value.Contains(".code", StringComparison.Ordinal) || value.StartsWith("code", StringComparison.Ordinal)) return SemanticPointerKind.CodeBlock;
        if (value.Contains(".p", StringComparison.Ordinal) || value.StartsWith('p')) return SemanticPointerKind.Paragraph;
        return SemanticPointerKind.Section;
    }

    public static SemanticPointer? GetContainingSectionPointer(SemanticPointer pointer)
    {
        var value = pointer.Value;
        if (value is "frontmatter" || value.StartsWith('p') || value.StartsWith("code", StringComparison.Ordinal))
        {
            return null;
        }

        var index = value.LastIndexOf('.');
        if (index < 0)
        {
            return null;
        }

        var prefix = value[..index];
        return char.IsDigit(prefix[^1]) ? new SemanticPointer(prefix) : null;
    }

    [GeneratedRegex(@"^(frontmatter|(?:\d+(?:\.\d+)*)(?:\.(?:p|code)\d+)?|p\d+|code\d+)$", RegexOptions.Compiled)]
    private static partial Regex PointerRegex();
}
