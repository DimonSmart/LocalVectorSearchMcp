# DimonSmart.LocalVectorSearchMcp

Local MCP server for indexing one project's Markdown files and searching them through SQLite FTS5 lexical search, sqlite-vec vector search, and hybrid Reciprocal Rank Fusion.

## MVP Scope

One project equals one MCP server instance, one detected project root, one configured knowledge base root, and one project-local SQLite index. Separate projects use separate server instances and cannot select or search each other's content through the API.

The MVP indexes `.md` files, stores documents, Markdown elements, chunks, FTS rows, sqlite-vec vectors, and index metadata in SQLite, and exposes MCP tools:

- `kb_reindex`
- `kb_status`
- `kb_search`
- `kb_read`

Not included in the MVP: PDF/DOCX import, web UI, file watcher, Git history indexing, reranker, remote HTTP MCP server, authentication, multi-user mode, background daemon mode, JSON config, `kb_find_files`, and advanced Markdown table/list parsing.

## Install

```bash
dotnet tool install --global DimonSmart.LocalVectorSearchMcp
```

## Add to Claude Code

Run from your project root:

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp
```

## Add to Codex

Run from your project root:

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp
```

By default the server indexes all `*.md` files under the project root and stores the local SQLite index in `.local-vector-search-mcp/index.db`.

When launched from Claude Code, the project root is detected from `CLAUDE_PROJECT_DIR`. Otherwise the current working directory is used.

## Configure Without YAML

Server options can be passed after the MCP client separator:

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --root docs \
    --embedding-endpoint http://localhost:11434/v1 \
    --embedding-model bge-m3:latest
```

Exclude folders explicitly when you want them excluded:

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --exclude "**/node_modules/**" \
    --exclude "**/.git/**"
```

Supported server configuration options:

```text
--config <path>
--root <path>
--storage-path <path>
--embedding-endpoint <url>
--embedding-model <model>
--include <glob>
--exclude <glob>
```

`--include` and `--exclude` are repeatable. If at least one CLI include or exclude is provided, that list replaces the YAML/default list.

## YAML Configuration

YAML remains supported as an advanced configuration scenario:

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp --config local-vector-search-mcp.yml
```

Effective configuration is built in this order:

```text
default config
explicit --config YAML, if provided
CLI options
path resolution
validation
```

Example YAML:

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

CLI options override YAML values:

```bash
local-vector-search-mcp \
  --config local-vector-search-mcp.yml \
  --root docs \
  --embedding-model bge-m3:latest
```

## Ollama

The default embedding endpoint is Ollama's OpenAI-compatible API:

```bash
ollama pull bge-m3:latest
ollama serve
```

First reindex requires the embedding endpoint to be reachable. `apiKey` defaults to `ollama` for local Ollama-compatible usage.

Remote embedding endpoints are rejected unless `allowRemoteEndpoint: true` is explicitly set in YAML.

## CLI Maintenance

```bash
local-vector-search-mcp --status
local-vector-search-mcp --reindex
local-vector-search-mcp --reindex --force
```

From source:

```bash
dotnet run --project src/DimonSmart.LocalVectorSearchMcp.Server -- --status
dotnet run --project src/DimonSmart.LocalVectorSearchMcp.Server -- --reindex
```

With YAML:

```bash
local-vector-search-mcp --config ./local-vector-search-mcp.yml --status
local-vector-search-mcp --config ./local-vector-search-mcp.yml --reindex --force
```

## Search Modes

Lexical search uses SQLite FTS5 and BM25. Vector search uses sqlite-vec. Hybrid search combines lexical and vector ranks through Reciprocal Rank Fusion, so raw BM25 scores and vector distances are not mixed directly.

Changing the embedding model, embedding dimensions, chunker version, embedding text builder version, or chunking settings requires a forced rebuild of the index:

```bash
local-vector-search-mcp --reindex --force
```

## Verification

```bash
dotnet build
dotnet test
```

Release CI also publishes and smoke-tests Windows x64, Linux x64, macOS arm64, and macOS x64 self-contained binaries.
