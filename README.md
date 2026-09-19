# LocalVectorSearchMcp

**Give Codex, Claude Code, and ChatGPT a local, editable, project-aware Markdown workbench.**

LocalVectorSearchMcp is a local MCP server that indexes one project's Markdown files into a project-local SQLite database. Agents can search, read focused semantic slices, edit by semantic pointer, manage Markdown files, and navigate the workspace.

It helps an agent find the right specification, architectural decision, guide, or invariant without loading the entire documentation set into its context.

## Why use it?

Large repositories often contain the answer, but not in the file the agent happens to open first.

- Exact text search finds identifiers, API names, and quoted terminology.
- Vector search finds conceptually related documentation even when wording differs.
- Hybrid search combines both result lists through Reciprocal Rank Fusion.
- `kb_read` returns focused Markdown slices instead of forcing the agent to read whole files.
- Element-level optimistic concurrency prevents an agent from overwriting a semantic target that changed after it was read, while unrelated document edits do not block the patch.
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
MCP tools over stdio
```

Claude Code and Codex can launch the stdio server directly. ChatGPT can use the same MCP surface through OpenAI Secure MCP Tunnel, where the external `tunnel-client` bridges ChatGPT to the local stdio process.

## Quick start

### 1. Install the tool and embedding model

```bash
dotnet tool install --global DimonSmart.LocalVectorSearchMcp
ollama pull bge-m3:latest
```

Make sure Ollama is running before the first reindex.

### 2. Connect your client

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

#### ChatGPT

Keep LocalVectorSearchMcp as a local stdio server and bridge it with OpenAI Secure MCP Tunnel. For the tunnel scenario, use an explicit configuration with absolute knowledge-base and storage paths.

See [ChatGPT via Secure MCP Tunnel](docs/clients/chatgpt-secure-tunnel.md).

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
| Editable content | Markdown, guarded by exact element fingerprints |
| Navigation | File listing and heading outlines |
| Transport | MCP over stdio |
| Clients | Claude Code, Codex, ChatGPT via OpenAI Secure MCP Tunnel |
| Default embeddings | Local Ollama-compatible endpoint |

The MCP server exposes ten workspace tools plus two transfer diagnostics:

- `kb_status` — inspect the current index.
- `kb_reindex` — build or rebuild the index.
- `kb_search` — run optionally path-scoped lexical, semantic, or hybrid search.
- `kb_read` — read indexed Markdown from a semantic pointer and receive its `sourceHash`.
- `kb_patch` — atomically replace, insert before/after, or delete semantic elements.
- `kb_create`, `kb_move`, `kb_delete` — manage Markdown source files.
- `kb_list_files` — list Markdown files and non-indexed assets.
- `kb_outline` — return a deterministic heading tree.
- `debug_receive_file` — verify ChatGPT → MCP file handoff without persisting the received bytes.
- `debug_return_test_image` — verify MCP → ChatGPT image-content transfer with a tiny PNG.

## Local-first and project-isolated

Each server process belongs to one project and one configured Markdown root. It cannot select or search another project's index through the MCP API.

The default embedding endpoint is local Ollama. Remote embedding endpoints are rejected unless they are explicitly enabled in YAML. Document text, embedding text, API keys, and raw vectors are not logged.

## Documentation

- [Getting started](docs/getting-started.md)
- [Claude Code setup](docs/clients/claude-code.md)
- [Codex setup](docs/clients/codex.md)
- [ChatGPT via Secure MCP Tunnel](docs/clients/chatgpt-secure-tunnel.md)
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

Markdown files remain the source of truth. A mutation first changes the source file and then synchronizes the derived SQLite index. Internal semantic pointers remain logical structural addresses. Public concrete-element pointers add a 16-character XxHash64 fingerprint of the exact source span, for example `1.2.p2~8f41c721d904a8bc`. `kb_patch` validates those anchors against the current source and can relocate an unchanged element after a structural shift. `sourceHash` is still returned by `kb_read` for whole-file operations such as `kb_move` and `kb_delete`.

### `kb_patch` examples

The MCP method accepts one `request` object. Inside it, `operations` is always an array, even when applying a single operation. Concrete-element pointers must use the fingerprinted anchors returned by `kb_read`, `kb_search`, or `kb_outline`. `kb_patch` does not accept `expectedSourceHash`.

Replace:

```json
{
  "request": {
    "path": "chapter.md",
    "operations": [
      {
        "kind": "replace",
        "pointer": "1.2.p2~8f41c721d904a8bc",
        "markdown": "New paragraph."
      }
    ]
  }
}
```

Insert after:

```json
{
  "request": {
    "path": "chapter.md",
    "operations": [
      {
        "kind": "insert_after",
        "pointer": "1.2.p2~8f41c721d904a8bc",
        "markdown": "Additional paragraph."
      }
    ]
  }
}
```

Delete:

```json
{
  "request": {
    "path": "chapter.md",
    "operations": [
      {
        "kind": "delete",
        "pointer": "1.2.p2~8f41c721d904a8bc"
      }
    ]
  }
}
```

Supported `kind` values are `replace`, `insert_before`, `insert_after`, and `delete`. Replace and insert operations require `markdown`; delete does not. The special `document` pointer remains unhashed and can be used with `insert_before` or `insert_after` at document boundaries.

An unrelated edit elsewhere in the file does not invalidate an element anchor. If the target moved because content was inserted above it, `kb_patch` relocates it only when the same element kind and exact fingerprint identify exactly one current element. If the target itself changed, disappeared, or relocation is ambiguous, the patch is rejected.

## Current scope

The current version supports local Markdown and discovers ordinary assets through file listing without indexing them. Remote access from ChatGPT is supported through OpenAI Secure MCP Tunnel, which externally launches and bridges the existing local stdio server. Two diagnostic tools can transfer a temporary file into the server for hashing and return a test PNG, but they do not persist binary assets or edit Markdown image references.

PDF/DOCX/OCR, binary editing, image embeddings, a web UI, Git history indexing, direct remote HTTP MCP transport, application-level authentication, multi-user mode, CRDT, and automatic merge are outside the current scope.
