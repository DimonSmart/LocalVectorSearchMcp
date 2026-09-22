# LocalVectorSearchMcp

**Give Codex, Claude Code, and ChatGPT a local, editable, project-aware Markdown workbench.**

LocalVectorSearchMcp is a local MCP server that indexes one project's Markdown files into a project-local SQLite database. Agents can search, read focused semantic slices, edit by semantic pointer, manage Markdown files, navigate the workspace, and manage ordinary image assets.

It helps an agent find the right specification, architectural decision, guide, or invariant without loading the entire documentation set into its context.

## Why use it?

Large repositories often contain the answer, but not in the file the agent happens to open first.

- Exact text search finds identifiers, API names, and quoted terminology.
- Vector search finds conceptually related documentation even when wording differs.
- Hybrid search combines both result lists through Reciprocal Rank Fusion.
- `kb_read` returns focused Markdown slices instead of forcing the agent to read whole files.
- Element-level optimistic concurrency prevents an agent from overwriting a semantic target that changed after it was read, while unrelated document edits do not block the patch.
- Optional file watching keeps the index synchronized with edits from VS Code, Obsidian, or other editors.
- PNG, JPEG, WebP, and GIF files can be stored as non-indexed workspace assets under `images/`.
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

images/ assets
       ↓
save / list / load / delete
       ↓
not indexed
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
Attach this PNG and save it as cover.png in the project images folder.
Return the Markdown image reference.
```

## Capabilities

| Capability | Implementation |
|---|---|
| Exact search | SQLite FTS5 with BM25 |
| Semantic search | sqlite-vec |
| Hybrid ranking | Reciprocal Rank Fusion |
| Storage | Project-local SQLite |
| Indexed content | Markdown |
| Editable content | Markdown, guarded by exact element and subtree hashes |
| Image assets | PNG, JPEG, WebP, GIF under `images/` |
| Navigation | File listing, image listing, and heading outlines |
| Transport | MCP over stdio |
| Clients | Claude Code, Codex, ChatGPT via OpenAI Secure MCP Tunnel |
| Default embeddings | Local Ollama-compatible endpoint |

The MCP server exposes fourteen tools. MCP reindexing is asynchronous: `kb_reindex` starts or joins the single active reindex and returns immediately, while `kb_status.indexing` reports progress and the last outcome. The CLI `--reindex` command remains synchronous and exits only after indexing finishes.


- `kb_status` — inspect the current index.
- `kb_reindex` — start a background build or rebuild; monitor `kb_status.indexing` for progress and the final result.
- `kb_search` — run optionally path-scoped lexical, semantic, or hybrid search.
- `kb_read` — read indexed Markdown from a semantic pointer and receive its `sourceHash`.
- `kb_patch` — atomically replace, insert before/after, or delete semantic elements.
- `kb_create`, `kb_move`, `kb_delete` — manage Markdown source files.
- `kb_list_files` — list Markdown files and non-indexed assets.
- `kb_outline` — return a deterministic heading tree.
- `kb_save_image` — save an image supplied through the OpenAI file parameter into `images/`.
- `kb_list_images` — list supported image assets recursively with cursor pagination.
- `kb_load_image` — return an existing image as a real MCP image content block.
- `kb_delete_image` — delete one image asset under `images/`.

For low-level MCP or connector calls, a tool with no user arguments still uses an explicit empty `arguments` object. The canonical `kb_status` wire call is:

```json
{
  "name": "kb_status",
  "arguments": {}
}
```

Clients should not rely on omitting the argument object; `{}` is the interoperable contract for parameterless tools.

## Image assets

Image assets are intentionally simple files in the workspace rather than a media subsystem.

`kb_save_image` accepts PNG, JPEG, WebP, and GIF, detects the real format from the file signature, and limits the incoming file to 25 MiB. It saves only directly under:

```text
<knowledgeBase.root>/images/
```

An explicit `fileName` must be a portable basename. It cannot choose a subdirectory, contain traversal/path components, use a Windows device name, or declare an extension that conflicts with the detected format. If the name is omitted, a safe basename from the OpenAI file metadata is used when possible; otherwise a generated name is used.

Existing files are never overwritten. Collisions are resolved with suffixes:

```text
cover.png
cover-2.png
cover-3.png
```

The final move uses no-overwrite semantics, so concurrent saves cannot replace an existing asset.

The response contains `path`, detected `mimeType`, byte count, lowercase SHA-256, and a Markdown reference such as:

```markdown
![Cover](images/cover.png)
```

`kb_list_images` and `kb_load_image` are read-only and work even when `knowledgeBase.allowWrites` is false. Listing is recursive so externally created paths such as `images/chapter-01/diagram.webp` can be discovered. The default page size is 50 and the maximum is 200.

`kb_load_image` revalidates size, signature, and extension/signature agreement, computes SHA-256, and returns the real bytes as MCP `ImageContentBlock`.

`kb_save_image` and `kb_delete_image` require:

```yaml
knowledgeBase:
  allowWrites: true
```

Image paths are guarded against traversal, symlink/junction/reparse-point escape, and access outside `images/`. The OpenAI temporary download URL must be absolute HTTPS and cannot resolve to loopback/private/link-local destinations. Redirect targets are revalidated. Signed download URLs and query secrets are not logged or returned in controlled errors.

Images are visible through `kb_list_files` as `asset`, but they do not create search chunks, enter FTS, call embeddings, or change the Markdown source set.

## Local-first and project-isolated

Each server process belongs to one project and one configured Markdown root. It cannot select or search another project's index through the MCP API.

The default embedding endpoint is local Ollama. Remote embedding endpoints are rejected unless they are explicitly enabled in YAML. Document text, embedding text, API keys, raw vectors, and signed image download URLs are not logged.

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

Markdown files remain the source of truth for indexed knowledge. A Markdown mutation commits the source file first and then schedules synchronization of the derived SQLite index. The mutation call does not wait for reconciliation to finish; `indexSynchronized: false` means the source commit succeeded but derived-index synchronization has not yet been confirmed. Internal semantic pointers remain logical structural addresses. Public concrete-element pointers use `logical~selfHash~subtreeHash`, where both values are 16-character lowercase XxHash64 hashes over exact UTF-8 source. `selfHash` covers the element itself; heading `subtreeHash` covers the complete section source range owned by `replace_section`, while leaf hashes are equal. `kb_patch` relocates only by kind plus `selfHash`; `replace_section` additionally validates `subtreeHash`. `sourceHash` is still returned by `kb_read` for whole-file operations such as `kb_move` and `kb_delete`.

Image save/delete are independent asset mutations and do not synchronize the Markdown index.

### `kb_patch` examples

The MCP method accepts one `request` object. Inside it, `operations` is always an array, even when applying a single operation. New concrete-element pointers should use the canonical v2 anchors returned by `kb_read`, `kb_search`, or `kb_outline`. Legacy `logical~selfHash` anchors remain accepted for navigation and `Self` mutations, but `replace_section` requires the v2 subtree hash. `kb_patch` does not accept `expectedSourceHash`.

Use `replace_element` to replace exactly one editable Markdown element. For a heading, it changes only the heading itself and keeps the existing section body. The replacement must parse as exactly one editable element, and a heading replacement must keep the same heading level. Legacy `replace` remains supported as a deprecated alias with the same validation.

Use `replace_section` to rewrite a complete heading section atomically. The target must be a canonical v2 heading anchor with both `selfHash` and `subtreeHash`. The replaced range starts at that heading and continues through all nested content until the next heading with level less than or equal to the target level, or EOF. The replacement must start with a heading of the same level and may contain only deeper headings after it.

Replace one element:

```json
{
  "request": {
    "path": "chapter.md",
    "operations": [
      {
        "kind": "replace_element",
        "pointer": "1.2.p2~8f41c721d904a8bc~8f41c721d904a8bc",
        "markdown": "New paragraph."
      }
    ]
  }
}
```

Replace a whole section:

```json
{
  "request": {
    "path": "chapter.md",
    "operations": [
      {
        "kind": "replace_section",
        "pointer": "1.2~0123456789abcdef~fedcba9876543210",
        "markdown": "## Updated section\n\nNew body.\n\n### Nested heading\n\nNested body."
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
        "pointer": "1.2.p2~8f41c721d904a8bc~8f41c721d904a8bc",
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
        "pointer": "1.2.p2~8f41c721d904a8bc~8f41c721d904a8bc"
      }
    ]
  }
}
```

Supported `kind` values are `replace`, `replace_element`, `replace_section`, `insert_before`, `insert_after`, and `delete`. `replace`, `replace_element`, `replace_section`, and insert operations require `markdown`; delete does not. The special `document` pointer remains unhashed and can be used with `insert_before` or `insert_after` at document boundaries. Existing valid single-element `replace` requests remain compatible; multi-element replacement through `replace` is intentionally rejected and should use `replace_section` or other explicit operations.

An unrelated edit elsewhere in the file does not invalidate a `Self` mutation when the target `selfHash` is still valid. If the target moved because content was inserted above it, `kb_patch` relocates it only when the same element kind and exact `selfHash` identify exactly one current element. `replace_section` then also verifies the original `subtreeHash`, so edits anywhere in the owned section range—including raw/non-indexed Markdown—are rejected rather than overwritten.

## Current scope

The current version supports indexed local Markdown plus ordinary image assets under `images/`. Remote access from ChatGPT is supported through OpenAI Secure MCP Tunnel, which externally launches and bridges the existing local stdio server.

PDF/DOCX/OCR, arbitrary binary upload, image embeddings/search, image resizing/transcoding/thumbnails, automatic Markdown image insertion, a media database, a web UI, Git history indexing, direct remote HTTP MCP transport, application-level authentication, multi-user mode, CRDT, and automatic merge are outside the current scope.
