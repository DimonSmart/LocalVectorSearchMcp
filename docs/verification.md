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

## Developer verification

From the repository root:

```bash
dotnet build
dotnet test
```

Release CI additionally builds and smoke-tests self-contained binaries for Windows x64, Linux x64, macOS arm64, and macOS x64.
