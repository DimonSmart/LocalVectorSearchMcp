# Configuration

LocalVectorSearchMcp works without a configuration file. Built-in defaults are enough for a project whose Markdown files are under the project root and whose embeddings are provided by local Ollama.

Use command-line options for simple overrides. Use YAML when you need chunking, search, security, or other advanced settings.

## Configuration precedence

Effective configuration is built in this order:

```text
built-in defaults
explicit --config YAML, when provided
CLI options
path resolution
validation
```

CLI values override YAML values.

`local-vector-search-mcp.yml` is not loaded implicitly. Pass it explicitly with `--config`.

## CLI options

```text
--config <path>
--project-root <absolute-existing-directory>
--root <path>
--storage-path <path>
--embedding-provider <openai-compatible|none>
--search-mode <lexical|semantic|hybrid>
--watch-files | --no-watch-files
--embedding-endpoint <url>
--embedding-model <model>
--include <glob>
--exclude <glob>
```

`--include` and `--exclude` are repeatable. When at least one CLI include or exclude is supplied, that CLI list replaces the corresponding YAML or default list.

## Configure through Claude Code and Codex

The server arguments after the client separator are identical.

### Claude Code

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --root docs \
    --storage-path .cache/local-vector-search/index.db \
    --embedding-endpoint http://localhost:11434/v1 \
    --embedding-model bge-m3:latest
```

### Codex

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp \
    --root docs \
    --storage-path .cache/local-vector-search/index.db \
    --embedding-endpoint http://localhost:11434/v1 \
    --embedding-model bge-m3:latest
```

## Offline lexical-only configuration

Use these options to search Markdown without Ollama, embeddings, sqlite-vec runtime
initialization, or network access:

```bash
local-vector-search-mcp \
  --project-root /absolute/path/to/my-project \
  --root .idd/intent \
  --embedding-provider none \
  --search-mode lexical \
  --watch-files \
  --reindex
```

Or configure it in YAML:

```yaml
embedding:
  provider: none

search:
  defaultMode: lexical

knowledgeBase:
  root: .idd/intent
  allowWrites: false
  watchFiles: true
```

Embedding endpoint, API key, model and vector dimensions are not required in this mode.
If the explicitly selected source directory is missing, startup fails rather than
creating it. The SQLite index lives under the project root by default, not in the source directory.

## Include and exclude patterns

### Claude Code

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --include "**/*.md" \
    --exclude "**/node_modules/**" \
    --exclude "**/.git/**"
```

### Codex

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp \
    --include "**/*.md" \
    --exclude "**/node_modules/**" \
    --exclude "**/.git/**"
```

Patterns use filesystem glob semantics.

## YAML configuration

Create `local-vector-search-mcp.yml`:

```yaml
server:
  name: local-vector-search-mcp

storage:
  path: ./.local-vector-search-mcp/index.db

embedding:
  provider: openai-compatible
  endpoint: http://localhost:11434/v1
  apiKey: ollama
  model: bge-m3:latest
  dimensions: null
  batchSize: 16
  allowRemoteEndpoint: false
  timeoutSeconds: 120

chunking:
  maxChunkBytes: 4096
  maxElements: 20
  includeHeadingContext: true
  includeFrontMatter: true

search:
  defaultMode: hybrid
  semanticCandidatePoolSize: 50
  lexicalCandidatePoolSize: 50
  maxResults: 10
  rrfK: 60

knowledgeBase:
  root: .
  allowWrites: false
  watchFiles: false
  include:
    - "**/*.md"
  exclude: []
```

Register it with either client.

### Claude Code

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --config local-vector-search-mcp.yml
```

### Codex

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp \
    --config local-vector-search-mcp.yml
```

## Project root and path resolution

The project root is detected as follows:

1. Explicit absolute `--project-root`, which must point to an existing directory.
2. Non-empty `CLAUDE_PROJECT_DIR`.
3. Otherwise, the server process working directory.

The knowledge-base root is selected in this order:

1. CLI `--root`.
2. YAML `knowledgeBase.root`.
3. Detected project root.

The storage path is selected in this order:

1. CLI `--storage-path`.
2. YAML `storage.path`.
3. `<project-root>/.local-vector-search-mcp/index.db`.

Relative `knowledgeBase.root` and `storage.path` are resolved against the selected
project root. A relative `--config` path stays relative to the **process working
directory** unless `--project-root` is explicitly passed, in which case it is
resolved against that explicit root.

The knowledge-base root is not the same as the project root: file access through
MCP tools is guarded to the configured knowledge-base directory. Traversal and
symlink/junction/reparse-point escapes from that directory are rejected.

A project-local .NET tool manifest is discovered by the .NET CLI using its
working directory. Supplying `--project-root` to the MCP executable does **not**
change .NET tool manifest discovery.

## Writes and external file watching

Both capabilities are disabled by default. Enable them explicitly for a Markdown workbench:

```yaml
knowledgeBase:
  allowWrites: true
  watchFiles: true
```

`allowWrites` enables `kb_patch`, `kb_create`, `kb_move`, `kb_delete`, `kb_save_image`, `kb_delete_image`, and `kb_move_image`. Mutation paths must be relative `.md` paths under the configured root; existing destinations and paths through symbolic links, junctions, or reparse points are rejected. Existing save/list/load/delete image paths remain under `images/`; `kb_move_image` accepts supported image source/target paths anywhere inside the configured root but rejects every outside-root resolution and never overwrites a target.

`watchFiles` observes external Markdown create, edit, rename, and delete events and reconciles affected index entries after a short debounce. It does not require `allowWrites`, because the watcher reads changes made by other applications rather than creating them.

Search requests can independently narrow already indexed documents with optional `includeGlobs` and `excludeGlobs`. These filters do not change the configured source set.

## Embedding endpoints

The default embedding endpoint is local Ollama:

```text
http://localhost:11434/v1
```

The default model is:

```text
bge-m3:latest
```

Remote endpoints are rejected unless YAML explicitly sets:

```yaml
embedding:
  allowRemoteEndpoint: true
```

Before enabling a remote endpoint, consider whether project documentation is allowed to leave the machine. API keys, document text, embedding text, and raw vectors are not logged by the server.

## Search modes

- `lexical` — SQLite FTS5 and BM25.
- `semantic` — sqlite-vec nearest-neighbor retrieval.
- `hybrid` — Reciprocal Rank Fusion over lexical and semantic ranks.

The default is `hybrid` for backward compatibility.
When `embedding.provider: none`, request `lexical` for FTS5 without a warning.
A `hybrid` request falls back to FTS5 with an explicit warning and reports
the effective result mode as `lexical`. An explicit `semantic` request fails
with a controlled error because embeddings are disabled.

If the embedding endpoint is temporarily unavailable, `hybrid` automatically falls back to lexical FTS5 results and returns a warning that semantic search is unavailable. Explicit `semantic` mode does not silently fall back; it returns a controlled tool error. `kb_reindex` starts the background operation; if embeddings cannot be produced, the operation stops cleanly and its warning is exposed in `kb_status.indexing.last.result`, while already indexed content remains available for lexical search.

## When a forced rebuild is required

Changing any of the following makes the existing index incompatible:

- schema version;
- index mode (`lexical` versus `vector-enabled`);
- embedding model;
- embedding dimensions;
- chunker version;
- embedding text builder version;
- chunking settings.

Rebuild it with:

```bash
local-vector-search-mcp --reindex --force
```

The same operation is available through `kb_reindex` with `force: true`.


### Image asset paths

All five image tools are scoped to safe workspace-relative paths under `knowledgeBase.root`, independently of Markdown include/exclude patterns. Saves default to `directory: "images"`; use `directory: "."` for the root or a nested directory such as `assets/figures`. Optional `documentPath: "chapters/intro.md"` makes the returned Markdown destination relative to that document rather than the root. Image moves never rewrite Markdown. Explicit `updateReferences: true` is rejected; use `kb_patch` separately. Image operations do not reindex Markdown.
