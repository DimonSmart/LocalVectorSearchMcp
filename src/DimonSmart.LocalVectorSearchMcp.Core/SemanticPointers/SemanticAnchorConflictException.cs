namespace DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

public enum SemanticAnchorConflictReason
{
    SelfHashMismatch,
    SubtreeHashMismatch,
    AmbiguousSemanticPointer,
    SemanticTargetNotFound,
    MissingSubtreeHash
}

public sealed class SemanticAnchorConflictException(
    SemanticAnchorConflictReason reason,
    string message) : Exception(message)
{
    public SemanticAnchorConflictReason Reason { get; } = reason;
}
