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
--root <path>
--storage-path <path>
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

1. Non-empty `CLAUDE_PROJECT_DIR`.
2. Otherwise, the server process working directory.

The knowledge-base root is selected in this order:

1. CLI `--root`.
2. YAML `knowledgeBase.root`.
3. Detected project root.

The storage path is selected in this order:

1. CLI `--storage-path`.
2. YAML `storage.path`.
3. `<project-root>/.local-vector-search-mcp/index.db`.

Relative paths are resolved against the detected project root.

All file access is restricted to the configured project root. Traversal and absolute paths outside that root are rejected.

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

The default is `hybrid`.

## When a forced rebuild is required

Changing any of the following makes the existing index incompatible:

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
