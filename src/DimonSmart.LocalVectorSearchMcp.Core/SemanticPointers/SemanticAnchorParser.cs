namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public static class SemanticAnchorParser
{
    public static SemanticAnchor Parse(string value)
    {
        var separator = value.IndexOf('~');
        if (separator < 0)
        {
            return new SemanticAnchor(SemanticPointerParser.Parse(value));
        }

        if (separator == 0
            || separator != value.LastIndexOf('~'))
        {
            throw InvalidFingerprint();
        }

        var logicalValue = value[..separator];
        var fingerprint = value[(separator + 1)..];
        if (fingerprint.Length != 16 || !fingerprint.All(Uri.IsHexDigit))
        {
            throw InvalidFingerprint();
        }

        var logicalPointer = SemanticPointerParser.Parse(logicalValue);
        if (SemanticPointerParser.GetKind(logicalPointer) == SemanticPointerKind.Document)
        {
            throw InvalidFingerprint();
        }

        return new SemanticAnchor(logicalPointer, fingerprint.ToLowerInvariant());
    }

    private static SemanticPointerFormatException InvalidFingerprint()
        => new("Invalid semantic pointer fingerprint.");
}
