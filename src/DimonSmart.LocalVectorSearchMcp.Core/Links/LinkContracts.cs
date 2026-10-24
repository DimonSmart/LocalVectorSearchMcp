namespace DimonSmart.LocalVectorSearchMcp.Core.Links;

/// <summary>One occurrence in the exact indexed Markdown revision.</summary>
public sealed record LinkOccurrence(
    long Id, string SourcePath, string TargetPath, string Destination,
    string? Fragment, string Text, string Kind, int Line, int Column,
    int Offset, int Length, string IndexedSourceHash,
    int Distance = 1, string Direction = "outgoing", string Status = "resolved");

public sealed record LinkNode(string Path, int Distance);

public sealed record LinksRequest(
    string? Path = null, string Direction = "both", int Depth = 1,
    bool IncludeBroken = false, bool BrokenOnly = false,
    int PageSize = 100, string? Cursor = null);

public sealed record LinksResponse(
    string? Path, string Direction, int Depth, bool IncludeBroken,
    bool BrokenOnly, IReadOnlyList<LinkNode> Nodes,
    IReadOnlyList<LinkOccurrence> Links, long GraphRevision,
    bool SynchronizationPending, string? LastError,
    string? NextCursor, bool Truncated);

public sealed record LinkGraphStatus(
    bool Initialized, bool Ready, int Documents, int Occurrences,
    long Revision, int PendingChanges, string? LastError);

public sealed class LinkGraphException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ILinkGraphService
{
    Task SynchronizeAsync(IReadOnlyList<DimonSmart.LocalVectorSearchMcp.Core.Markdown.MarkdownSourceDocument> documents,
        bool rebuild, CancellationToken cancellationToken);
    Task<bool> ReconcileAsync(string relativePath,
        DimonSmart.LocalVectorSearchMcp.Core.Markdown.MarkdownSourceDocument? document,
        CancellationToken cancellationToken);
    Task<LinksResponse> QueryAsync(LinksRequest request, CancellationToken cancellationToken);
    Task<LinkGraphStatus> GetStatusAsync(CancellationToken cancellationToken);
    void MarkPending(string path);
}
