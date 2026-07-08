namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record SemanticPointer(string Value)
{
    public override string ToString() => Value;
}
