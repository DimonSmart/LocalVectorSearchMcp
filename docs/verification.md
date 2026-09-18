# Verification

Use this checklist to confirm that LocalVectorSearchMcp is installed, connected, indexed, and returning useful results.

## 1. Verify the executable

```bash
dotnet tool list --global
local-vector-search-mcp --status
```

The global tool list should contain `DimonSmart.LocalVectorSearchMcp`.

Run the status command from the project root so the reported root and storage path are meaningful.

## 2. Verify the embedding endpoint

For the default Ollama setup:

```bash
ollama list
```

Confirm that `bge-m3:latest` is installed and that Ollama is running.

The first reindex requires the embedding endpoint to be reachable.

## 3. Verify the MCP registration

### Claude Code

```bash
claude mcp list
claude mcp get local-vector-search
```

Inside Claude Code:

```text
/mcp
```

### Codex

```bash
codex mcp list
codex mcp get local-vector-search
```

Inside the Codex terminal UI:

```text
/mcp
```

The server should be listed as enabled and connected.

## 4. Verify tool discovery

Ask the client:

```text
List the tools exposed by the local-vector-search MCP server.
```

Expected tools:

```text
kb_status
kb_reindex
kb_search
kb_read
kb_patch
kb_create
kb_move
kb_delete
kb_list_files
kb_outline
```

## 5. Build and inspect the index

Ask:

```text
Use kb_status to inspect the documentation index.
If no documents are indexed, call kb_reindex.
Then report the indexed document count and index path.
```

Alternatively:

```bash
local-vector-search-mcp --reindex
local-vector-search-mcp --status
```

Confirm that the project contains:

```text
.local-vector-search-mcp/index.db
```

## 6. Verify lexical search

Choose a distinctive phrase that literally occurs in a Markdown file:

```text
Use kb_search in lexical mode to find "Reciprocal Rank Fusion".
Show the best result and its source path.
```

A relevant result containing the phrase confirms FTS indexing and exact retrieval.

## 7. Verify semantic search

Ask for the same concept without copying its wording:

```text
Use kb_search in semantic mode to find documentation explaining
how exact-text and vector result rankings are combined.
```

A relevant result confirms embeddings and sqlite-vec retrieval.

## 8. Verify hybrid search and reading

```text
Use kb_search in hybrid mode to find the architectural description
of search ranking. Read the most relevant source with kb_read and
summarize it with the source path.
```

This validates the normal end-to-end workflow: search, semantic pointer, and focused Markdown reading.

## 9. Verify editable workbench behavior

Use a disposable workspace with `knowledgeBase.allowWrites: true` and `knowledgeBase.watchFiles: true`.

1. Create a Markdown file with `kb_create` and confirm it is immediately returned by `kb_search`.
2. Read it with `kb_read` and confirm concrete element pointers use `<logical-pointer>~<16 lowercase hex>`.
3. Keep one paragraph anchor, manually edit a different paragraph, then call `kb_patch` with the old target anchor. The patch should succeed and preserve the unrelated manual edit without `expectedSourceHash`.
4. Keep another paragraph anchor, manually insert a paragraph above it so its logical pointer shifts, then patch through the old anchor. The unique unchanged target should be relocated automatically.
5. Change the target paragraph itself and confirm the old anchor is rejected without overwriting the human edit.
6. Create two identical relocation candidates and confirm an old shifted anchor is rejected as ambiguous.
7. Inspect `kb_outline` and `kb_search`; their concrete pointers and search read hints should also contain canonical fingerprints.
8. Use `kb_move` and `kb_delete` with the latest `sourceHash` from `kb_read`, confirming their whole-file revision contract remains unchanged.
9. Use `kb_list_files`; add a binary asset and confirm it appears as `asset` but is not indexed.

Through MCP tool discovery, confirm `kb_patch` has no `expectedSourceHash` property while `kb_move` and `kb_delete` still expose theirs.

## Developer verification

From the repository root:

```bash
dotnet build
dotnet test
```

Release CI additionally builds and smoke-tests self-contained binaries for Windows x64, Linux x64, macOS arm64, and macOS x64.
