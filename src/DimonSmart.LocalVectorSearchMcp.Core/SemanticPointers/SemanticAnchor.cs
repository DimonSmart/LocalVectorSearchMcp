using DimonSmart.LocalVectorSearchMcp.Core.Markdown;

namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed record SemanticAnchor(SemanticPointer LogicalPointer, string? Fingerprint = null)
{
    public bool IsDocument => SemanticPointerParser.GetKind(LogicalPointer) == SemanticPointerKind.Document;

    public static SemanticAnchor FromElement(MarkdownElement element)
        => element.Kind == MarkdownElementKind.Document
            ? new SemanticAnchor(element.Pointer)
            : new SemanticAnchor(element.Pointer, SemanticFingerprint.Compute(element.Text));

    public override string ToString()
        => Fingerprint is null
            ? LogicalPointer.Value
            : $"{LogicalPointer.Value}~{Fingerprint}";
}
