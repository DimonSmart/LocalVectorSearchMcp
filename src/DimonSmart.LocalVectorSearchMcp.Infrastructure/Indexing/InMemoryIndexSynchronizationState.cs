using System.Collections.Concurrent;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;

public sealed class InMemoryIndexSynchronizationState : IIndexSynchronizationState
{
    private readonly ConcurrentDictionary<string, DirtyRevision> dirty =
        new(StringComparer.OrdinalIgnoreCase);
    private long nextGeneration;
    private string? lastError;

    public long MarkDirty(string relativePath, string error)
    {
        var generation = Interlocked.Increment(ref nextGeneration);
        dirty[relativePath] = new DirtyRevision(generation, error);
        Volatile.Write(ref lastError, error);
        return generation;
    }

    public void MarkFailed(
        string relativePath,
        long generation,
        string error)
    {
        while (dirty.TryGetValue(relativePath, out var current))
        {
            if (current.Generation != generation)
            {
                return;
            }

            var failed = current with { Error = error };
            if (dirty.TryUpdate(relativePath, failed, current))
            {
                Volatile.Write(ref lastError, error);
                return;
            }
        }
    }

    public void MarkSynchronized(string relativePath, long generation)
    {
        while (dirty.TryGetValue(relativePath, out var current))
        {
            if (current.Generation != generation)
            {
                return;
            }

            var collection =
                (ICollection<KeyValuePair<string, DirtyRevision>>)dirty;
            if (collection.Remove(
                    new KeyValuePair<string, DirtyRevision>(
                        relativePath,
                        current)))
            {
                ClearLastErrorWhenClean();
                return;
            }
        }
    }

    public void MarkSynchronized(string relativePath)
    {
        dirty.TryRemove(relativePath, out _);
        ClearLastErrorWhenClean();
    }

    public IndexSynchronizationStatus GetStatus()
    {
        var paths = dirty.Keys
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToList();
        return new IndexSynchronizationStatus(
            paths.Count,
            paths,
            Volatile.Read(ref lastError));
    }

    private void ClearLastErrorWhenClean()
    {
        if (dirty.IsEmpty)
        {
            Volatile.Write(ref lastError, null);
        }
    }

    private sealed record DirtyRevision(long Generation, string Error);
}
