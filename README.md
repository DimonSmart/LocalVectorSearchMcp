# LocalVectorSearchMcp

**Give Codex and Claude Code a local, editable, project-aware Markdown workbench.**

LocalVectorSearchMcp is a local MCP server that indexes one project's Markdown files into a project-local SQLite database. Agents can search, read focused semantic slices, edit by semantic pointer, manage Markdown files, and navigate the workspace.

It helps an agent find the right specification, architectural decision, guide, or invariant without loading the entire documentation set into its context.

## Why use it?

Large repositories often contain the answer, but not in the file the agent happens to open first.

- Exact text search finds identifiers, API names, and quoted terminology.
- Vector search finds conceptually related documentation even when wording differs.
- Hybrid search combines both result lists through Reciprocal Rank Fusion.
- `kb_read` returns focused Markdown slices instead of forcing the agent to read whole files.
- Optimistic concurrency prevents an agent from overwriting a file changed after it was read.
- Optional file watching keeps the index synchronized with edits from VS Code, Obsidian, or other editors.
- Every project gets its own local SQLite index.
- No cloud database or mandatory YAML configuration is required.

## How it works

```text
Workspace Markdown (source of truth)
       ↓
Markdown elements and search chunks
       ↓
SQLite FTS5 + sqlite-vec
       ↓
Scoped retrieval + semantic editing
       ↓
MCP tools for Codex and Claude Code
```

## Quick start

### 1. Install the tool and embedding model

```bash
dotnet tool install --global DimonSmart.LocalVectorSearchMcp
ollama pull bge-m3:latest
```

Make sure Ollama is running before the first reindex.

### 2. Connect your coding agent

Run the command from the project whose documentation should be indexed.

#### Claude Code

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp
```

#### Codex

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp
```

### 3. Ask the agent to initialize and use the index

```text
Check the local documentation index status. Reindex it if necessary,
then find the project's main architectural decisions and summarize them
with file references.
```

By default, the server indexes `*.md` files under the detected project root and stores the index in `.local-vector-search-mcp/index.db`.

See [Getting started](docs/getting-started.md) for the complete setup and verification flow.

## Example tasks

```text
Before changing the indexing pipeline, find the documented architectural
decisions and constraints that apply to it.
```

```text
Search the project documentation for rules governing persisted state.
Summarize the relevant invariants and cite the source files.
```

```text
Find documentation related to FileAccessProvider and explain how
session-level overrides are expected to work.
```

## Capabilities

| Capability | Implementation |
|---|---|
| Exact search | SQLite FTS5 with BM25 |
| Semantic search | sqlite-vec |
| Hybrid ranking | Reciprocal Rank Fusion |
| Storage | Project-local SQLite |
| Indexed content | Markdown |
| Editable content | Markdown, guarded by exact source hashes |
| Navigation | File listing and heading outlines |
| Transport | MCP over stdio |
| Clients | Claude Code and Codex |
| Default embeddings | Local Ollama-compatible endpoint |

The MCP server exposes ten focused tools:

- `kb_status` — inspect the current index.
- `kb_reindex` — build or rebuild the index.
- `kb_search` — run optionally path-scoped lexical, semantic, or hybrid search.
- `kb_read` — read indexed Markdown from a semantic pointer and receive its `sourceHash`.
- `kb_patch` — atomically replace, insert before/after, or delete semantic elements.
- `kb_create`, `kb_move`, `kb_delete` — manage Markdown source files.
- `kb_list_files` — list Markdown files and non-indexed assets.
- `kb_outline` — return a deterministic heading tree.

## Local-first and project-isolated

Each server process belongs to one project and one configured Markdown root. It cannot select or search another project's index through the MCP API.

The default embedding endpoint is local Ollama. Remote embedding endpoints are rejected unless they are explicitly enabled in YAML. Document text, embedding text, API keys, and raw vectors are not logged.

## Documentation

- [Getting started](docs/getting-started.md)
- [Claude Code setup](docs/clients/claude-code.md)
- [Codex setup](docs/clients/codex.md)
- [Configuration](docs/configuration.md)
- [Maintenance and updates](docs/maintenance.md)
- [Verification](docs/verification.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Product specification](.idd/intent/0001.spec-product-overview.md)
- [Architecture decision](.idd/intent/0006.adr-mcp-sqlite-hybrid-architecture.md)

## Editable workspaces

Writes and external file watching are explicit opt-ins:

```yaml
knowledgeBase:
  root: .
  allowWrites: true
  watchFiles: true
```

Markdown files remain the source of truth. A mutation first changes the source file and then synchronizes the derived SQLite index. Semantic pointers address one document revision; mutation calls use the exact `sourceHash` returned by `kb_read` to detect concurrent human edits.

## Current scope

The current version supports local Markdown and discovers ordinary assets through file listing without indexing them. PDF/DOCX/OCR, binary editing, image embeddings, a web UI, Git history indexing, remote HTTP MCP transport, authentication, multi-user mode, CRDT, and automatic merge are outside the current scope.
