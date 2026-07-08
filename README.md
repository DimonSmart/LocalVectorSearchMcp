# DimonSmart.LocalVectorSearchMcp

Local MCP server for indexing configured Markdown folders and searching them through SQLite FTS5 lexical search, sqlite-vec vector search, and hybrid Reciprocal Rank Fusion.

## MVP Scope

The MVP indexes only `.md` files from configured knowledge base roots. It stores documents, Markdown elements, chunks, FTS rows, sqlite-vec vectors, and index metadata in SQLite. It exposes MCP tools:

- `kb_reindex`
- `kb_status`
- `kb_search`
- `kb_read`

Not included in the MVP: PDF/DOCX import, web UI, file watcher, Git history indexing, reranker, remote HTTP MCP server, authentication, multi-user mode, background daemon mode, JSON config, `kb_find_files`, and advanced Markdown table/list parsing.

## Configuration

Default config path is `local-vector-search-mcp.yml`. Override it with `--config` or `LOCAL_VECTOR_SEARCH_MCP_CONFIG`.

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

knowledgeBases:
  - name: current-project
    root: .
    include:
      - "**/*.md"
    exclude:
      - "**/bin/**"
      - "**/obj/**"
      - "**/.git/**"
      - "**/node_modules/**"
      - "**/.local-vector-search-mcp/**"
```

## Ollama

The default embedding endpoint is Ollama's OpenAI-compatible API:

```bash
ollama pull bge-m3:latest
ollama serve
```

First reindex requires the embedding endpoint to be reachable. `apiKey` is required for OpenAI-compatible clients; for local Ollama it can be a placeholder such as `ollama`.

## CLI

```bash
dotnet run --project src/DimonSmart.LocalVectorSearchMcp.Server -- --config ./local-vector-search-mcp.yml --reindex
dotnet run --project src/DimonSmart.LocalVectorSearchMcp.Server -- --config ./local-vector-search-mcp.yml --status
```

## MCP Config

From source:

```json
{
  "mcpServers": {
    "local-vector-search": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "src/DimonSmart.LocalVectorSearchMcp.Server",
        "--",
        "--config",
        "local-vector-search-mcp.yml"
      ]
    }
  }
}
```

Published executable:

```json
{
  "mcpServers": {
    "local-vector-search": {
      "command": "C:/Tools/DimonSmart.LocalVectorSearchMcp/DimonSmart.LocalVectorSearchMcp.Server.exe",
      "args": [
        "--config",
        "C:/Projects/MyProject/local-vector-search-mcp.yml"
      ]
    }
  }
}
```

## Search Modes

Lexical search uses SQLite FTS5 and BM25. Vector search uses sqlite-vec. Hybrid search combines lexical and vector ranks through Reciprocal Rank Fusion, so raw BM25 scores and vector distances are not mixed directly.

Changing embedding model or dimensions requires a forced rebuild of the index.

## Verification

```bash
dotnet build
dotnet test
```
