namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticAnchorParser
{
    public static SemanticAnchor Parse(string value)
    {
        var parts = value.Split('~');
        if (parts.Length == 1)
        {
            return new SemanticAnchor(SemanticPointerParser.Parse(value));
        }

        if (parts.Length is not (2 or 3)
            || string.IsNullOrWhiteSpace(parts[0])
            || !IsHash(parts[1])
            || (parts.Length == 3 && !IsHash(parts[2])))
        {
            throw InvalidFingerprint();
        }

        var logicalPointer = SemanticPointerParser.Parse(parts[0]);
        if (SemanticPointerParser.GetKind(logicalPointer) == SemanticPointerKind.Document)
        {
            throw InvalidFingerprint();
        }

        return new SemanticAnchor(
            logicalPointer,
            parts[1].ToLowerInvariant(),
            parts.Length == 3 ? parts[2].ToLowerInvariant() : null);
    }

    private static bool IsHash(string value)
        => value.Length == 16 && value.All(Uri.IsHexDigit);

    private static SemanticPointerFormatException InvalidFingerprint()
        => new("Invalid semantic pointer fingerprint.");
}
