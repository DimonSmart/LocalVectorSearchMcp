# LocalVectorSearchMcp

<!-- mcp-name: io.github.dimonsmart/local-vector-search-mcp -->

**Give Codex, Claude Code, and ChatGPT a local, editable, project-aware Markdown workbench.**

LocalVectorSearchMcp is a local MCP server that indexes one project's Markdown files into a project-local SQLite database. Agents can search, read focused semantic slices, edit by semantic pointer, manage Markdown files, navigate the workspace, and manage ordinary image assets.

It helps an agent find the right specification, architectural decision, guide, or invariant without loading the entire documentation set into its context.

## Why use it?

Large repositories often contain the answer, but not in the file the agent happens to open first.

- Exact text search finds identifiers, API names, and quoted terminology.
- Vector search finds conceptually related documentation even when wording differs.
- Hybrid search combines both result lists through Reciprocal Rank Fusion.
- `kb_read` returns focused slices from the current Markdown source instead of forcing the agent to read whole files or wait for indexing.
- Element-level optimistic concurrency prevents an agent from overwriting a semantic target that changed after it was read, while unrelated document edits do not block the patch.
- Optional file watching keeps the index synchronized with edits from VS Code, Obsidian, or other editors.
- PNG, JPEG, WebP, and GIF assets may reside anywhere under the configured workspace root; `images/` is the default save directory.
- Every project gets its own local SQLite index.
- No cloud database or mandatory YAML configuration is required.

## How it works

```text
Workspace Markdown (source of truth)
       ↓
Markdown elements and search chunks
       ↓
SQLite FTS5 (+ sqlite-vec when embeddings are enabled)
       ↓
Scoped retrieval + semantic editing
       ↓
MCP tools over stdio

images/ assets
       ↓
save / list / load / delete / safe move
       ↓
not indexed
```

Claude Code and Codex can launch the stdio server directly. ChatGPT can use the same MCP surface through OpenAI Secure MCP Tunnel, where the external `tunnel-client` bridges ChatGPT to the local stdio process.

## Quick start: offline lexical search

Install the .NET tool (requires .NET 10):

```bash
dotnet tool install --global DimonSmart.LocalVectorSearchMcp
```

From the project you want to index, run the following. This needs no Ollama server,
no remote embedding endpoint and no API key:

```bash
local-vector-search-mcp --project-root "$PWD" \
  --embedding-provider none --search-mode lexical --reindex
```

Register a project-specific MCP server using the same arguments:

```bash
codex mcp add local-vector-search -- \
  local-vector-search-mcp --project-root "$PWD" \
  --embedding-provider none --search-mode lexical --watch-files
```

The SQLite FTS5 index is stored in `.local-vector-search-mcp/index.db` under
the project root. Markdown remains the source of truth. The server is read-only
for Markdown and images unless `knowledgeBase.allowWrites: true` is explicitly set in YAML.

See the [IDD integration example](docs/integrations/intent-driven-development.md)
for `.idd/intent`, explicit paths, `dnx`, and Claude Code.

## Optional hybrid/semantic quick start

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
| Image assets | PNG, JPEG, WebP, GIF; all image tools support any safe in-root path; saves default to `images/` |
| Navigation | File listing, image listing, and heading outlines |
| Transport | MCP over stdio |
| Clients | Claude Code, Codex, ChatGPT via OpenAI Secure MCP Tunnel |
| Default embeddings | Local Ollama-compatible endpoint; use `embedding.provider: none` for offline FTS5-only mode |

The MCP server exposes fifteen tools. MCP reindexing is asynchronous: `kb_reindex` starts or joins the single active reindex and returns immediately, while `kb_status.indexing` reports progress and the last outcome. The CLI `--reindex` command remains synchronous and exits only after indexing finishes.


- `kb_status` — inspect the current index.
- `kb_reindex` — start a background build or rebuild; monitor `kb_status.indexing` for progress and the final result.
- `kb_search` — run optionally path-scoped lexical, semantic, or hybrid search against the derived index; each result identifies its indexed revision with `indexedSourceHash`.
- `kb_read` — read the current Markdown source from a semantic pointer and receive the exact source revision `sourceHash`.
- `kb_patch` — atomically edit individual semantic elements or heading sections, replace a unique inline fragment, and insert before/after elements.
- `kb_create`, `kb_move`, `kb_delete` — manage Markdown source files.
- `kb_list_files` — list Markdown files and non-indexed assets.
- `kb_outline` — return a deterministic heading tree.
- `kb_save_image` — save an OpenAI file asset into `images/` by default, or another in-root directory.
- `kb_list_images` — list supported image assets recursively with cursor pagination.
- `kb_load_image` — return an existing image as a real MCP image content block.
- `kb_delete_image` — delete a single in-root binary image asset.
- `kb_move_image` — move only a supported binary image asset within `knowledgeBase.root`; never edit Markdown.

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

All image tools support PNG, JPEG, WebP, and GIF assets anywhere within `knowledgeBase.root`. The `images/` directory is only the default for `kb_save_image`; image assets are not indexed. Image paths are workspace-relative, subject to reparse-point/traversal checks, and never escape the configured root. No image operation reads, modifies, or indexes Markdown. `knowledgeBase.include/exclude` only scopes Markdown indexing.

`kb_save_image(file, fileName?, altText?, directory?, documentPath?)` accepts an OpenAI file parameter via `_meta["openai/fileParams"] = ["file"]`. Its secure HTTPS downloader validates redirects, private addresses, magic bytes, declared MIME, and a hard 25 MiB size limit. It publishes through a same-directory temporary file and an atomic no-overwrite move; filename collisions append `-2`, `-3`, etc. `directory` defaults to `images` and permits `.` for the root; `documentPath` is an optional .md workspace path, even if not created yet.

The save response contains `path`, canonical `mimeType`, `bytes`, lowercase SHA-256, and `markdown`. The `path` is always relative to the workspace root, but `markdown` uses a destination relative to the directory of `documentPath` when provided. For example, saving `assets/figures/a.png` for `chapters/intro.md` returns `![Figure](../assets/figures/a.png)`. Unsafe destination URI characters (such as `%`, `#` and `?`) are escaped. The server does not insert the snippet into any Markdown document.

`kb_list_images` recursively enumerates the entire root, regardless of whether `images/` exists. Pagination defaults to 50 (maximum 200), ordered case-insensitively then ordinally; its cursor format is `v2` and old `v1` cursors must restart. `kb_load_image` is read-only, validates image size/format/signature, and returns an MCP image content block first, followed by JSON metadata, without structured output.

`kb_move_image` accepts `sourcePath`, `targetPath`, optional `expectedSha256`, and a deprecated compatibility-only `updateReferences` flag. When absent or false, only the binary file moves. Explicit `updateReferences=true` fails before mutation; the tool does not scan or alter Markdown. Its response has only `previousPath`, `path`, and `sha256`. `kb_delete_image` removes one binary file even if Markdown still references it; it does not remove parent directories.

Save, move, and delete require `knowledgeBase.allowWrites=true`; list and load remain available in read-only mode. None of these operations synchronizes the Markdown index. Download URL secrets are never logged.

To update references, use separate tool calls: `kb_save_image` then `kb_patch(replace_fragment)`; or `kb_move_image` then `kb_patch` on each affected document. For replacement, use save → patch → optional delete. These steps are **not one transaction**: after a successful move and failed patch, the asset is already at its new path, so reread current semantic anchors and retry only the failed Markdown patch. Missing image files never invalidate a Markdown patch.

## Local-first and project-isolated

Each server process belongs to one project and one configured Markdown root. It cannot select or search another project's index through the MCP API.

The default embedding endpoint is local Ollama. Remote embedding endpoints are rejected unless they are explicitly enabled in YAML. Document text, embedding text, API keys, raw vectors, and signed image download URLs are not logged.

## Documentation

- [Getting started](docs/getting-started.md)
- [Offline IDD integration](docs/integrations/intent-driven-development.md)
- [MCP Registry discovery](https://registry.modelcontextprotocol.io/v0.1/servers?search=io.github.dimonsmart%2Flocal-vector-search-mcp) (entry available after the first registry release)
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

Markdown files remain the source of truth for indexed knowledge. `kb_read` and `kb_outline` parse the current source directly; `kb_search` uses the derived SQLite index and may temporarily return an older indexed revision. A Markdown mutation commits the source file first and then schedules synchronization of the derived SQLite index. The mutation call does not wait for reconciliation to finish; `indexSynchronized: false` means the source commit succeeded but derived-index synchronization has not yet been confirmed. Internal semantic pointers remain logical structural addresses. Public concrete-element pointers use `logical~selfHash~subtreeHash`, where both values are 16-character lowercase XxHash64 hashes over exact UTF-8 source. `selfHash` covers the element itself; heading `subtreeHash` covers the complete section source range owned by `replace_section` and `delete_section`, while leaf hashes are equal. `kb_patch` relocates only by kind plus `selfHash`; `replace_section` and `delete_section` additionally validate `subtreeHash`. `sourceHash` is still returned by `kb_read` for whole-file operations such as `kb_move` and `kb_delete`.

For inline edits, `replace_fragment` matches non-empty `oldMarkdown` exactly once inside a Self-anchored parent, verifies the parent block kind is preserved, and supports disjoint fragments in one atomic batch. `insert_before(document)` places text after YAML front matter when present. For structural edits, `replace_element` replaces exactly one Markdown element and `delete` removes exactly one element; deleting a heading with `delete` preserves its section body. `replace_section` replaces a heading together with its complete owned section subtree, while `delete_section` removes that whole subtree. Both section operations require a canonical v2 heading pointer and validate `subtreeHash`.

All image tools are independent binary mutations and never schedule Markdown indexing. Only explicit Markdown mutations such as `kb_patch` synchronize the derived index.

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

The current version supports indexed local Markdown plus ordinary image assets. Save/list/load/delete use `images/`; `kb_move_image` may relocate a supported image to another safe path inside `knowledgeBase.root`. Remote access from ChatGPT is supported through OpenAI Secure MCP Tunnel, which externally launches and bridges the existing local stdio server.

PDF/DOCX/OCR, arbitrary binary upload, image embeddings/search, image resizing/transcoding/thumbnails, automatic Markdown image insertion, a media database, a web UI, Git history indexing, direct remote HTTP MCP transport, application-level authentication, multi-user mode, CRDT, and automatic merge are outside the current scope.
