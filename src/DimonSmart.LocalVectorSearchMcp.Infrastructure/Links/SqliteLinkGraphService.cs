using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Links;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Links;

/// <summary>Independent, reproducible SQLite index; never opens sqlite-vec or invokes embeddings.</summary>
public sealed class SqliteLinkGraphService(
    SqliteConnectionFactory factory, KnowledgeBasePathGuard guard)
    : ILinkGraphService
{
    private const int MaxVisitedNodes = 2000;
    private const int MaxExaminedEdges = 30000;
    private readonly MarkdownLinkExtractor extractor = new(guard);
    private readonly ConcurrentDictionary<string, byte> pending =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private string? lastError;

    public void MarkPending(string path) => pending[path] = 0;

    private static async Task InitializeAsync(SqliteConnection db, CancellationToken ct)
    {
        await db.ExecuteAsync("""
            create table if not exists link_meta (
              key text primary key, value text not null
            );
            insert or ignore into link_meta(key,value) values('schema','1'),('revision','0');
            create table if not exists link_documents (
              path text primary key, source_hash text not null, indexed_at_utc text not null
            );
            create table if not exists link_occurrences (
              id integer primary key,
              source_path text not null,
              target_path text not null,
              destination text not null,
              fragment text null,
              link_text text not null,
              link_kind text not null,
              source_line integer not null,
              source_column integer not null,
              source_offset integer not null,
              source_length integer not null
            );
            create index if not exists ix_link_source on link_occurrences(source_path);
            create index if not exists ix_link_target on link_occurrences(target_path);
            create table if not exists link_anchors (
              document_path text not null,
              anchor text not null,
              primary key(document_path, anchor)
            );
            """, ct);
    }

    private static Task BumpAsync(SqliteConnection db, SqliteTransaction tx, CancellationToken ct)
        => db.ExecuteAsync(
            "update link_meta set value=cast(value as integer)+1 where key='revision'",
            ct, transaction: tx);

    public async Task SynchronizeAsync(IReadOnlyList<MarkdownSourceDocument> documents,
        bool rebuild, CancellationToken cancellationToken)
    {
        await using var db = factory.Open();
        await InitializeAsync(db, cancellationToken);
        var paths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            paths.Add(document.RelativePath);
            await ReconcileInDatabaseAsync(db, document.RelativePath, document,
                rebuild, cancellationToken);
        }

        var oldPaths = new List<string>();
        var command = db.CreateCommand();
        command.CommandText = "select path from link_documents";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                if (!paths.Contains(reader.GetString(0))) oldPaths.Add(reader.GetString(0));
        foreach (var path in oldPaths)
            await ReconcileInDatabaseAsync(db, path, null, false, cancellationToken);
        await db.ExecuteAsync(
            "insert or replace into link_meta(key,value) values('ready','1')",
            cancellationToken);
    }

    public async Task<bool> ReconcileAsync(string relativePath,
        MarkdownSourceDocument? document, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = factory.Open();
            await InitializeAsync(db, cancellationToken);
            var changed = await ReconcileInDatabaseAsync(
                db, relativePath, document, false, cancellationToken);
            pending.TryRemove(relativePath, out _);
            if (pending.IsEmpty) Volatile.Write(ref lastError, null);
            return changed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            pending[relativePath] = 0;
            Volatile.Write(ref lastError, ex.GetType().Name);
            throw;
        }
    }

    private async Task<bool> ReconcileInDatabaseAsync(SqliteConnection db, string path,
        MarkdownSourceDocument? document, bool rebuild, CancellationToken ct)
    {
        // Validate source paths too: a workspace symlink must never widen filesystem access.
        _ = guard.ResolveMarkdownPath(path);
        var previous = await db.ScalarStringAsync(
            "select source_hash from link_documents where path=$p",
            [("$p", path)], ct);
        if (document is null && previous is null) return false;
        if (document is not null && !rebuild && previous == document.SourceHash) return false;

        // Parse fully before touching the previous consistent indexed revision.
        var parsed = document is null ? null : extractor.Parse(document);
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync("delete from link_occurrences where source_path=$p",
            ct, [("$p", path)], tx);
        await db.ExecuteAsync("delete from link_anchors where document_path=$p",
            ct, [("$p", path)], tx);
        if (document is null)
        {
            await db.ExecuteAsync("delete from link_documents where path=$p",
                ct, [("$p", path)], tx);
        }
        else
        {
            await db.ExecuteAsync("""
                insert into link_documents(path,source_hash,indexed_at_utc)
                values($p,$h,$t)
                on conflict(path) do update set
                  source_hash=excluded.source_hash,indexed_at_utc=excluded.indexed_at_utc
                """, ct, [("$p", path), ("$h", document.SourceHash),
                    ("$t", DateTimeOffset.UtcNow.ToString("O"))], tx);
            foreach (var anchor in parsed!.Anchors)
                await db.ExecuteAsync(
                    "insert or ignore into link_anchors(document_path,anchor) values($p,$a)",
                    ct, [("$p", path), ("$a", anchor)], tx);
            foreach (var link in parsed.Links)
                await db.ExecuteAsync("""
                    insert into link_occurrences(
                      source_path,target_path,destination,fragment,link_text,link_kind,
                      source_line,source_column,source_offset,source_length)
                    values($s,$t,$d,$f,$x,$k,$l,$c,$o,$n)
                    """, ct,
                    [("$s", path), ("$t", link.TargetPath), ("$d", link.Destination),
                     ("$f", link.Fragment), ("$x", link.Text), ("$k", link.Kind),
                     ("$l", link.Line), ("$c", link.Column),
                     ("$o", link.Offset), ("$n", link.Length)], tx);
        }
        await BumpAsync(db, tx, ct);
        await tx.CommitAsync(ct);
        pending.TryRemove(path, out _);
        return true;
    }

    public async Task<LinkGraphStatus> GetStatusAsync(CancellationToken ct)
    {
        await using var db = factory.Open();
        await InitializeAsync(db, ct);
        return new LinkGraphStatus(
            true, await ReadyAsync(db, null, ct),
            (int)(await db.ScalarLongAsync("select count(*) from link_documents", [], ct) ?? 0),
            (int)(await db.ScalarLongAsync("select count(*) from link_occurrences", [], ct) ?? 0),
            await RevisionAsync(db, null, ct), pending.Count, Volatile.Read(ref lastError));
    }

    private static async Task<bool> ReadyAsync(SqliteConnection db, SqliteTransaction? tx, CancellationToken ct)
        => await db.ScalarStringAsync("select value from link_meta where key='ready'",
            [], ct, tx) == "1";

    private static async Task<long> RevisionAsync(SqliteConnection db, SqliteTransaction? tx, CancellationToken ct)
        => await db.ScalarLongAsync("select cast(value as integer) from link_meta where key='revision'",
            [], ct, tx) ?? 0;

    private sealed record Edge(
        long Id, string Source, string Target, string Destination, string? Fragment,
        string Text, string Kind, int Line, int Column, int Offset, int Length,
        string Hash);

    private static async Task<List<Edge>> LoadEdgesAsync(SqliteConnection db,
        SqliteTransaction tx, string? path, string direction, int limit, CancellationToken ct)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            select o.id,o.source_path,o.target_path,o.destination,o.fragment,o.link_text,
                   o.link_kind,o.source_line,o.source_column,o.source_offset,o.source_length,
                   d.source_hash
            from link_occurrences o join link_documents d on d.path=o.source_path
            """;
        if (path is not null)
        {
            cmd.CommandText += direction switch
            {
                "outgoing" => " where o.source_path=$path",
                "incoming" => " where o.target_path=$path",
                _ => " where o.source_path=$path or o.target_path=$path"
            };
            cmd.AddParameter("$path", path);
        }
        cmd.CommandText += " order by o.id limit $limit";
        cmd.AddParameter("$limit", limit);
        var edges = new List<Edge>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            edges.Add(new Edge(reader.GetInt64(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5), reader.GetString(6),
                reader.GetInt32(7), reader.GetInt32(8),
                reader.GetInt32(9), reader.GetInt32(10), reader.GetString(11)));
        return edges;
    }

    private async Task<string> StatusAsync(SqliteConnection db, SqliteTransaction tx,
        Edge edge, Dictionary<string, string> cache, CancellationToken ct)
    {
        var path = edge.Target;
        if (!cache.TryGetValue(path, out var status))
        {
            string absolute;
            try { absolute = guard.ResolveMarkdownPath(path); }
            catch (KnowledgeBaseAccessException)
            {
                return "target_not_indexed";
            }
            if (!File.Exists(absolute)) status = "missing_document";
            else
            {
                var count = await db.ScalarLongAsync(
                    "select count(*) from link_documents where path=$p",
                    [("$p", path)], ct, tx);
                status = count > 0 ? "resolved" : "target_not_indexed";
            }
            cache[path] = status;
        }
        if (status != "resolved" || edge.Fragment is null || edge.Fragment.Length == 0)
            return status;
        var slug = MarkdownLinkExtractor.NormalizeFragment(edge.Fragment);
        if (slug.Length == 0) return "fragment_unverified";
        var exists = await db.ScalarLongAsync("""
            select count(*) from link_anchors where document_path=$p and anchor=$a
            """, [("$p", path), ("$a", slug)], ct, tx);
        return exists > 0 ? "resolved" : "anchor_not_found";
    }

    public async Task<LinksResponse> QueryAsync(LinksRequest request, CancellationToken ct)
    {
        var direction = (request.Direction ?? "both").ToLowerInvariant();
        if (direction is not ("outgoing" or "incoming" or "both")
            || request.Depth is < 1 or > 5 || request.PageSize is < 1 or > 500
            || (request.BrokenOnly && (request.Depth != 1 || direction != "both" && direction != "outgoing"))
            || (!request.BrokenOnly && string.IsNullOrWhiteSpace(request.Path))
            || (request.BrokenOnly && request.Path is null && request.Cursor is { Length: 0 }))
            throw new LinkGraphException("INVALID_ARGUMENT", "Invalid kb_links request parameters.");
        if (request.BrokenOnly) direction = "outgoing";
        string? path = null;
        if (request.Path is not null)
        {
            try
            {
                path = guard.ValidateRelativePath(request.Path);
                _ = guard.ResolveMarkdownPath(path);
                if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    throw new KnowledgeBaseAccessException("A Markdown path is required.");
            }
            catch (KnowledgeBaseAccessException)
            {
                throw new LinkGraphException("INVALID_PATH", "The requested Markdown path is unsafe.");
            }
        }
        var normalized = request with { Path = path, Direction = direction,
            IncludeBroken = request.IncludeBroken || request.BrokenOnly };
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(normalized with { Cursor = null, PageSize = 0 }))));

        await using var db = factory.Open();
        await InitializeAsync(db, ct);
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);
        if (!await ReadyAsync(db, tx, ct))
            throw new LinkGraphException("LINK_INDEX_NOT_READY", "Run kb_reindex to build the Markdown link index.");
        var revision = await RevisionAsync(db, tx, ct);
        var offset = 0;
        if (request.Cursor is not null)
        {
            try
            {
                var cursor = JsonSerializer.Deserialize<LinkCursor>(
                    Convert.FromBase64String(request.Cursor));
                if (cursor is null || cursor.Signature != signature || cursor.Offset < 0)
                    throw new FormatException();
                if (cursor.Revision != revision)
                    throw new LinkGraphException("CURSOR_EXPIRED", "Link graph changed; restart pagination.");
                offset = cursor.Offset;
            }
            catch (Exception e) when (e is FormatException or JsonException)
            {
                throw new LinkGraphException("INVALID_ARGUMENT", "Invalid kb_links cursor.");
            }
        }
        if (path is not null && direction != "incoming" && !request.BrokenOnly)
        {
            var indexed = await db.ScalarLongAsync(
                "select count(*) from link_documents where path=$p",
                [("$p", path)], ct, tx);
            if (indexed == 0)
                throw new LinkGraphException("DOCUMENT_NOT_FOUND", "The source Markdown document is not in the link index.");
        }

        var paths = new Dictionary<string, int>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (path is not null) { paths[path] = 0; queue.Enqueue(path); }
        var occurrences = new Dictionary<long, LinkOccurrence>();
        var statusCache = new Dictionary<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var examined = 0;
        var truncated = false;

        while (request.BrokenOnly && path is null ? examined == 0 : queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = request.BrokenOnly && path is null ? null : queue.Dequeue();
            var distance = current is null ? 0 : paths[current];
            if (distance >= normalized.Depth) continue;
            var capacity = MaxExaminedEdges - examined;
            if (capacity <= 0) { truncated = true; break; }
            var edges = await LoadEdgesAsync(db, tx, current, direction, capacity + 1, ct);
            if (edges.Count > capacity) { truncated = true; edges.RemoveAt(edges.Count - 1); }
            examined += edges.Count;
            foreach (var edge in edges)
            {
                var edgeDirection = current is not null
                    && current == edge.Target && current != edge.Source ? "incoming" : "outgoing";
                if (direction == "incoming") edgeDirection = "incoming";
                var status = await StatusAsync(db, tx, edge, statusCache, ct);
                if (normalized.BrokenOnly && status != "missing_document") continue;
                if (!normalized.BrokenOnly && !normalized.IncludeBroken
                    && status != "resolved" && edgeDirection != "incoming") continue;

                occurrences.TryAdd(edge.Id, new LinkOccurrence(edge.Id,
                    edge.Source, edge.Target, edge.Destination, edge.Fragment,
                    edge.Text, edge.Kind, edge.Line, edge.Column, edge.Offset,
                    edge.Length, edge.Hash, distance + 1, edgeDirection, status));

                if (current is null || normalized.BrokenOnly) continue;
                var next = edgeDirection == "incoming" ? edge.Source : edge.Target;
                if (status != "resolved" && edgeDirection == "outgoing") continue;
                if (paths.ContainsKey(next)) continue;
                if (paths.Count >= MaxVisitedNodes) { truncated = true; continue; }
                paths[next] = distance + 1;
                if (distance + 1 < normalized.Depth) queue.Enqueue(next);
            }
            if (truncated) break;
        }
        var sorted = occurrences.Values
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourcePath, StringComparer.Ordinal)
            .ThenBy(x => x.Line).ThenBy(x => x.Column).ThenBy(x => x.Id).ToList();
        if (offset > sorted.Count)
            throw new LinkGraphException("INVALID_ARGUMENT", "Cursor offset is outside the result.");
        var page = sorted.Skip(offset).Take(request.PageSize).ToArray();
        var next = offset + page.Length < sorted.Count
            ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                new LinkCursor(revision, signature, offset + page.Length)))
            : null;
        var nodes = paths.Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new LinkNode(pair.Key, pair.Value)).ToArray();
        await tx.CommitAsync(ct);
        return new LinksResponse(path, direction, normalized.Depth,
            normalized.IncludeBroken, normalized.BrokenOnly, nodes, page,
            revision, !pending.IsEmpty, Volatile.Read(ref lastError), next, truncated);
    }

    private sealed record LinkCursor(long Revision, string Signature, int Offset);
}
