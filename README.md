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
- PNG, JPEG, WebP, and GIF files can be stored as non-indexed assets throughout `knowledgeBase.root` (default `images/`).
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
| Image assets | PNG, JPEG, WebP, GIF; all image tools use root-relative paths, saving to `images/` by default |
| Navigation | File listing, image listing, and heading outlines |
| Transport | MCP over stdio |
| Clients | Claude Code, Codex, ChatGPT via OpenAI Secure MCP Tunnel |
| Default embeddings | Local Ollama-compatible endpoint; use `embedding.provider: none` for offline FTS5-only mode |

The MCP server exposes fifteen tools. MCP reindexing is asynchronous: `kb_reindex` starts or joins the single active reindex and returns immediately, while `kb_status.indexing` reports progress and the last outcome. The CLI `--reindex` command remains synchronous and exits only after indexing finishes.


- `kb_status` — inspect the current index.
- `kb_reindex` — start a background build or rebuild; monitor `kb_status.indexing` for progress and the final result.
- `kb_search` — run optionally path-scoped lexical, semantic, or hybrid search against the derived index; each result identifies its indexed revision with `indexedSourceHash`.
- `kb_read` — read the current Markdown source from a semantic pointer and receive the exact source revision `sourceHash`.
- `kb_patch` — atomically replace/delete individual semantic elements or complete heading sections, and insert before/after elements.
- `kb_create`, `kb_move`, `kb_delete` — manage Markdown source files.
- `kb_list_files` — list Markdown files and non-indexed assets.
- `kb_outline` — return a deterministic heading tree.
- `kb_save_image` — save an image supplied through the OpenAI file parameter (optional `targetPath`).
- `kb_list_images` — list supported image assets recursively with cursor pagination.
- `kb_load_image` — return an existing image as a real MCP image content block.
- `kb_delete_image` — delete one binary image under root without changing Markdown.
- `kb_move_image` — safely move one binary image anywhere inside root without changing Markdown references.

For low-level MCP or connector calls, a tool with no user arguments still uses an explicit empty `arguments` object. The canonical `kb_status` wire call is:

```json
{
  "name": "kb_status",
  "arguments": {}
}
```

Clients should not rely on omitting the argument object; `{}` is the interoperable contract for parameterless tools.

## Image assets

All five image tools work with PNG, JPEG, WebP and GIF files **anywhere under `knowledgeBase.root`**. Paths are root-relative; `.git/`, internal SQLite files, traversal and symlink/junction/reparse-point paths are forbidden. `images/` is the recommended default folder, not an API restriction. Images are ordinary assets and are never indexed or embedded.

`kb_save_image(file, fileName?, altText?, targetPath?)` requires `allowWrites=true` and preserves the OpenAI `openai/fileParams` metadata. Downloads remain HTTPS-only with SSRF/redirect protection, a 25 MiB streaming limit, MIME/signature checks and SHA-256. Without `targetPath`, saves default to `images/`, using `fileName` or source basename and numeric suffixes for collisions (`cover.png`, `cover-2.png`). Explicit `targetPath` includes the final file name and extension, may create intermediate safe directories, and **never renames or overwrites** an occupied destination (`ALREADY_EXISTS`). Combining `targetPath` and `fileName` is invalid.

The save result includes `path`, `mimeType`, `bytes`, lowercase `sha256`, and `markdown`. The generated Markdown destination is relative to the **workspace root**. For a document in `chapters/`, the caller must recompute and escape the relative destination from that document.

`kb_list_images(cursor?, pageSize?)` lists supported image extensions throughout the root, sorted case-insensitively with ordinal tie-breaking. Cursor pagination defaults to 50 items (range 1–200) and supports the last cursor item being deleted. `kb_load_image(path)` validates signature, extension and size; its MCP result contains a real `ImageContentBlock` first and a JSON metadata text block second, **without** `StructuredContent`.

`kb_move_image` renames or moves **only one binary image**, anywhere under root. Input is `{sourcePath,targetPath,expectedSha256?,updateReferences?}`. `expectedSha256` is an optional 64-digit hex optimistic concurrency check; mismatches produce `CONFLICT`. The deprecated `updateReferences` may be omitted or set to `false`; `true` produces `INVALID_ARGUMENT`. The response is `{previousPath,path,sha256}` and contains no Markdown/index fields.

`kb_delete_image(path)` simply deletes the supported binary file, without scanning for Markdown references, checking if it is used, or removing its parent directories. Its result remains `{path,deleted:true}`. Both binary move and delete can leave broken Markdown links. Image operations never schedule indexing.

To repair links, use separate `kb_read` / `kb_patch` calls on correctly addressable Markdown elements. A move-and-patch workflow is not a multi-file transaction: find relevant documents before the move, move the image, patch the necessary elements with current semantic anchors, and verify the new paths. Raw link constructs that cannot be addressed safely should not be rewritten by replacing arbitrary larger sections. Git can be used for recovery.

Saving, moving, and deleting require:

```yaml
knowledgeBase:
  allowWrites: true
```

Expected tool errors return `IsError=true` and a JSON text object with `code` and `message`, e.g. `INVALID_PATH`, `UNSUPPORTED_FORMAT`, `ALREADY_EXISTS`, `CONFLICT`, `PERMISSION_DENIED`. Image tools have no `indexSynchronized`; for Markdown mutations its value is `true` only when reconciliation has already been confirmed or was unnecessary, and `false` when source changes succeeded but reconciliation is still unconfirmed.
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

For structural edits, `replace_element` replaces exactly one Markdown element and `delete` removes exactly one element; deleting a heading with `delete` preserves its section body. `replace_section` replaces a heading together with its complete owned section subtree, while `delete_section` removes that whole subtree. Both section operations require a canonical v2 heading pointer and validate `subtreeHash`.

None of the five image tools changes Markdown or schedules Markdown index synchronization.

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

Supported `kind` values are `replace`, `replace_element`, `replace_section`, `delete_section`, `insert_before`, `insert_after`, and `delete`. `replace`, `replace_element`, `replace_section`, and insert operations require `markdown`; delete does not. The special `document` pointer remains unhashed and can be used with `insert_before` or `insert_after` at document boundaries. Existing valid single-element `replace` requests remain compatible; multi-element replacement through `replace` is intentionally rejected and should use `replace_section` or other explicit operations.

An unrelated edit elsewhere in the file does not invalidate a `Self` mutation when the target `selfHash` is still valid. If the target moved because content was inserted above it, `kb_patch` relocates it only when the same element kind and exact `selfHash` identify exactly one current element. `replace_section` then also verifies the original `subtreeHash`, so edits anywhere in the owned section range—including raw/non-indexed Markdown—are rejected rather than overwritten.

## Current scope

The current version supports indexed local Markdown plus ordinary image assets. All five image tools use `knowledgeBase.root` as the boundary; saving defaults to `images/`. Remote access from ChatGPT is supported through OpenAI Secure MCP Tunnel, which externally launches and bridges the existing local stdio server.

PDF/DOCX/OCR, arbitrary binary upload, image embeddings/search, image resizing/transcoding/thumbnails, automatic Markdown image insertion, a media database, a web UI, Git history indexing, direct remote HTTP MCP transport, application-level authentication, multi-user mode, CRDT, and automatic merge are outside the current scope.

### Addressable Markdown list items and atomic quotes

`kb_read` exposes individual list items as `list_item` using `li1`, `1.li2`, `1.li2.li1` plus the normal two-hash anchor suffix. Parent Text excludes nested item text. `delete` and `replace_element` remove/replace the **whole subtree** and require both current hashes. Adjacent insertions check self hash and accept one source-indented sibling with a compatible list marker; neither blank lines nor ordered numbers are silently normalized.

A stand-alone Markdown quote is one opaque `block_quote` with `qN` or `1.qN`, regardless of internal Markdown (including `> - item`). It can only be read, searched, deleted, inserted around, or replaced as a whole. Older paragraph pointers suppressed inside lists/quotes must be re-read.

List-item editing uses exact half-open source ranges: the subtree contains every descendant, whereas the parent's own text consists only of its non-child source segments. `replace_element` replaces the full subtree; `delete` consumes its terminating line ending when present; `insert_after` inserts after all descendants. No AST serialization or document-wide whitespace normalization is performed, and unmodified source bytes (including BOM and mixed EOL) are retained.

A list insertion or replacement must supply **one** item with the target's physical indentation and compatible marker style (ordered marker numbers may differ). Nested items, code, HTML comments, and other Markdown blocks are permitted when Markdig places them inside that item. Independent blocks outside the new subtree are rejected. For example, `- Updated\n\n<!-- detached -->` cannot replace a top-level item when the comment splits its list; `- Updated\n\n  <!-- nested -->` is allowed if Markdig assigns the comment to the item. A quote fragment similarly must form exactly one opaque quote without absorbing its neighbors.

The server re-parses the **entire proposed result** before writing: existing elements, non-addressable blocks, list-container ownership, parent relationships, and neighboring boundaries must survive. Ambiguous or overlapping edits (including multiple operations in one `kb_patch`) fail atomically with a structural error; the source file is left unchanged. Unsupported indentation, list splitting/merging, and quote-boundary ambiguity are intentionally rejected instead of being auto-formatted.


Search pointer and read hint still identify the first *chunk* element, not necessarily the exact list-item match. Chunker v5 requires explicit derived-index rebuild: `kb_reindex(force=true)` or CLI `--reindex --force`. No Markdown migration is performed.
