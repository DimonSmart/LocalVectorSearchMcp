using DimonSmart.LocalVectorSearchMcp.Core.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed record SemanticAnchor(
    SemanticPointer LogicalPointer,
    string? SelfHash = null,
    string? SubtreeHash = null)
{
    public string? Fingerprint => SelfHash;

    public bool IsDocument =>
        SemanticPointerParser.GetKind(LogicalPointer) == SemanticPointerKind.Document;

    public bool IsLegacy => SelfHash is not null && SubtreeHash is null;

    public static SemanticAnchor FromElement(MarkdownElement element)
    {
        if (element.Kind == MarkdownElementKind.Document)
        {
            return new SemanticAnchor(element.Pointer);
        }

        var selfHash = element.SelfHash ?? SemanticFingerprint.Compute(element.Text);
        var subtreeHash = element.SubtreeHash;
        if (subtreeHash is null)
        {
            if (element.Kind == MarkdownElementKind.Heading)
            {
                throw new InvalidOperationException(
                    "Heading subtree hash is not available. Use an element parsed from its complete source document.");
            }

            subtreeHash = selfHash;
        }

        return new SemanticAnchor(element.Pointer, selfHash, subtreeHash);
    }

    public override string ToString()
        => SelfHash is null
            ? LogicalPointer.Value
            : SubtreeHash is null
                ? $"{LogicalPointer.Value}~{SelfHash}"
                : $"{LogicalPointer.Value}~{SelfHash}~{SubtreeHash}";
}
