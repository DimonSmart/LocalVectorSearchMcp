# LocalVectorSearchMcp

**Give Codex and Claude Code fast, project-aware search over the Markdown documentation in your repository.**

LocalVectorSearchMcp is a local MCP server that indexes one project's Markdown files into a project-local SQLite database and exposes lexical, semantic, and hybrid search to coding agents.

It helps an agent find the right specification, architectural decision, guide, or invariant without loading the entire documentation set into its context.

## Why use it?

Large repositories often contain the answer, but not in the file the agent happens to open first.

- Exact text search finds identifiers, API names, and quoted terminology.
- Vector search finds conceptually related documentation even when wording differs.
- Hybrid search combines both result lists through Reciprocal Rank Fusion.
- `kb_read` returns focused Markdown slices instead of forcing the agent to read whole files.
- Every project gets its own local SQLite index.
- No cloud database or mandatory YAML configuration is required.

## How it works

```text
Project Markdown
       ↓
Markdown elements and search chunks
       ↓
SQLite FTS5 + sqlite-vec
       ↓
Lexical, semantic, or hybrid retrieval
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
| Transport | MCP over stdio |
| Clients | Claude Code and Codex |
| Default embeddings | Local Ollama-compatible endpoint |

The MCP server exposes four focused tools:

- `kb_status` — inspect the current index.
- `kb_reindex` — build or rebuild the index.
- `kb_search` — run lexical, semantic, or hybrid search.
- `kb_read` — read indexed Markdown from a semantic pointer.

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

## Current scope

The current version indexes project-local Markdown documentation. PDF and DOCX import, a web UI, file watching, Git history indexing, remote HTTP MCP transport, authentication, and multi-user mode are outside the current scope.
