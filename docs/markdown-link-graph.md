# Markdown link graph

`kb_links` finds explicit Markdown references independently of lexical/semantic search, embeddings, and sqlite-vec. The graph is derived from included `.md` files. A Markdown edit never happens through this tool.

First run `kb_reindex` with `{"request":{"scope":"changed"}}` (or use the CLI reindex command) to build missing graph rows. Use `kb_status.linkGraph` to check `ready`, document and occurrence counts, graph revision, pending changes and last error. An existing search index can be reused without regenerating embeddings.

Examples (MCP tool arguments):

```json
{"request":{"path":"docs/architecture.md","direction":"incoming"}}
```

```json
{"request":{"path":"docs/architecture.md","direction":"both","depth":2,"includeBroken":true,"pageSize":50}}
```

```json
{"request":{"brokenOnly":true}}
```

The response exposes unique `nodes` and specific `links` (including repeated occurrences), source and target paths, actual traversal direction, indexed source hash and one-based source line/column. The zero-based `offset` counts UTF-16 code units, excluding the BOM. Following an `anchor_not_found` link still locates its target Markdown file; the fragment is diagnostic.

`brokenOnly` identifies missing Markdown targets, not targets merely excluded from indexing. `target_not_indexed` means a target exists but is not a successfully indexed graph node. `includeBroken` includes unresolved outgoing occurrences; inbound navigation to absent files is allowed with a safe root-relative `.md` path. `fragment_unverified` is reserved for unsupported heading fragment forms.

`depth` is capped at 5, `pageSize` at 500. Send the returned `nextCursor` with the same parameters to continue. A change to graph revision invalidates the cursor and requires restarting the query. `truncated=true` means a server work limit was reached; no cursor promises to continue beyond that limit.

Only standard Markdown links resolving explicitly to `.md` paths are counted, including reference-style links, links inside tables and links to headings. HTML anchors, WikiLinks, external URIs, images and text in code blocks are not graph edges. Paths cannot escape the configured knowledge base or traverse symbolic links/reparse points. Relative link targets may contain `..` when they normalize safely within the root; `path` arguments may not.

Filesystem changes are eventually consistent through the existing watcher and mutation scheduler. The stored graph remains the last committed revision on failures. Moving a document never automatically repairs its incoming or outgoing Markdown link text.
