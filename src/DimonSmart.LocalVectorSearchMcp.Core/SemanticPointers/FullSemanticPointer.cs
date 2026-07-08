namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed record FullSemanticPointer(string Path, SemanticPointer Pointer)
{
    public override string ToString() => $"{Path}::{Pointer.Value}";
}
