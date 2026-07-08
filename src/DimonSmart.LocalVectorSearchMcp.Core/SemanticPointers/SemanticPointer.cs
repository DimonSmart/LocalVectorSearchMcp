namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public sealed record SemanticPointer(string Value)
{
    public override string ToString() => Value;
}
