namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public readonly record struct SourceRange(int Start, int Length)
{
    public int End => Start + Length;
}
