using System.Collections.Concurrent;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;

public sealed class InMemoryIndexSynchronizationState : IIndexSynchronizationState
{
    private readonly ConcurrentDictionary<string, string> dirty =
        new(StringComparer.OrdinalIgnoreCase);
    private string? lastError;

    public void MarkDirty(string relativePath, string error)
    {
        dirty[relativePath] = error;
        Volatile.Write(ref lastError, error);
    }

    public void MarkSynchronized(string relativePath)
    {
        dirty.TryRemove(relativePath, out _);
        if (dirty.IsEmpty) Volatile.Write(ref lastError, null);
    }

    public IndexSynchronizationStatus GetStatus()
    {
        var paths = dirty.Keys
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToList();
        return new IndexSynchronizationStatus(paths.Count, paths, Volatile.Read(ref lastError));
    }
}
