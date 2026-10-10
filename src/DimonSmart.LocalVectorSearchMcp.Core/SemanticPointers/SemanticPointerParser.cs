using System.Text.RegularExpressions;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static partial class SemanticPointerParser
{
    public static SemanticPointer Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new SemanticPointerFormatException($"Invalid semantic pointer: {value}");
        }

        return new SemanticPointer(value);
    }

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && PointerRegex().IsMatch(value);

    public static SemanticPointerKind GetKind(SemanticPointer pointer)
    {
        var value = pointer.Value;
        if (value == "document") return SemanticPointerKind.Document;
        if (value == "frontmatter") return SemanticPointerKind.FrontMatter;
        var segment = value[(value.LastIndexOf('.') + 1)..];
        if (segment.StartsWith("li", StringComparison.Ordinal)) return SemanticPointerKind.ListItem;
        if (segment.StartsWith("code", StringComparison.Ordinal)) return SemanticPointerKind.CodeBlock;
        if (segment.StartsWith('p')) return SemanticPointerKind.Paragraph;
        if (segment.StartsWith('q')) return SemanticPointerKind.BlockQuote;
        return SemanticPointerKind.Section;
    }

    public static SemanticPointer? GetContainingSectionPointer(SemanticPointer pointer)
    {
        var value = pointer.Value;
        if (value is "document" or "frontmatter") return null;

        var segments = value.Split('.');
        var count = 0;
        while (count < segments.Length && char.IsDigit(segments[count][0]))
        {
            count++;
        }

        if (count == 0) return null;
        if (count == segments.Length) count--;
        return count == 0
            ? null
            : new SemanticPointer(string.Join(".", segments.Take(count)));
    }

    public static SemanticPointer? GetParentListItemPointer(SemanticPointer pointer)
    {
        if (GetKind(pointer) != SemanticPointerKind.ListItem) return null;
        var value = pointer.Value;
        var separator = value.LastIndexOf('.');
        if (separator < 0) return null;
        var prefix = new SemanticPointer(value[..separator]);
        return GetKind(prefix) == SemanticPointerKind.ListItem ? prefix : null;
    }

    [GeneratedRegex(@"^(?:document|frontmatter|(?:[1-9]\d*(?:\.[1-9]\d*)*)(?:\.(?:p[1-9]\d*|code[1-9]\d*|q[1-9]\d*|li[1-9]\d*(?:\.li[1-9]\d*)*))?|p[1-9]\d*|code[1-9]\d*|q[1-9]\d*|li[1-9]\d*(?:\.li[1-9]\d*)*)$", RegexOptions.Compiled)]
    private static partial Regex PointerRegex();
}
