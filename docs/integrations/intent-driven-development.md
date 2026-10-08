# Using LocalVectorSearchMcp with Intent-Driven Development

LocalVectorSearchMcp is a general-purpose Markdown MCP server. It does not install IDD, create the `.idd` directory, or modify IDD configuration.

The examples below index only `.idd/intent/**/*.md`, with the SQLite database outside that source directory. They work offline: no Ollama process, remote API key, embedding request or cloud index is required.

## Minimal read-only YAML configuration

Create `local-vector-search-mcp.yml` at the project root:

```yaml
storage:
  path: .local-vector-search-mcp/index.db

embedding:
  provider: none

search:
  defaultMode: lexical

knowledgeBase:
  root: .idd/intent
  allowWrites: false
  watchFiles: true
  include:
    - "**/*.md"
  exclude: []
```

The source directory `.idd/intent` must already exist. The index file is derived state; ignore `.local-vector-search-mcp/` in Git. Watching external edits changes SQLite only. `allowWrites: false` disallows Markdown and image mutations through MCP.

## Direct invocation

Use an **absolute** project path. For example, from any current directory:

```bash
local-vector-search-mcp \
  --project-root /home/user/my-project \
  --config local-vector-search-mcp.yml \
  --reindex
```

Or avoid YAML altogether with an equivalent lexical-only launch:

```bash
local-vector-search-mcp \
  --project-root /home/user/my-project \
  --root .idd/intent \
  --embedding-provider none \
  --search-mode lexical \
  --watch-files
```

The `--config` path is resolved relative to the explicit project root when `--project-root` is supplied. In its absence, `--config` remains relative to the process working directory for compatibility. Relative `knowledgeBase.root` and `storage.path` resolve against the chosen project root.

## One-shot NuGet runner

With the .NET 10 SDK, once a matching version has been published to NuGet.org:

```bash
dnx DimonSmart.LocalVectorSearchMcp@0.2.0 --yes -- \
  --project-root /home/user/my-project \
  --root .idd/intent \
  --embedding-provider none \
  --search-mode lexical
```

Replace `0.2.0` with the published package version. One-shot execution can prompt for download approval without `--yes`. Local .NET tools instead use a manifest in the project's working directory; `--project-root` does not change where the .NET CLI searches for that manifest.

## Claude Code

```bash
claude mcp add local-vector-search \
  --scope local --transport stdio -- \
  dnx DimonSmart.LocalVectorSearchMcp@0.2.0 --yes -- \
  --project-root /home/user/my-project \
  --config local-vector-search-mcp.yml
```

## Codex

```bash
codex mcp add local-vector-search -- \
  dnx DimonSmart.LocalVectorSearchMcp@0.2.0 --yes -- \
  --project-root /home/user/my-project \
  --config local-vector-search-mcp.yml
```

Use one MCP process per project, with a unique project root. Each project keeps a separate SQLite database.

## Optional Ollama hybrid search

To enable semantic ranking and hybrid FTS5/vector search instead, replace the embedding and search sections:

```yaml
embedding:
  provider: openai-compatible
  endpoint: http://localhost:11434/v1
  apiKey: ollama
  model: bge-m3:latest

search:
  defaultMode: hybrid
```

Install Ollama and pull the model before indexing. Changing between vector-enabled and lexical-only indexes changes their manifest compatibility. Run `--reindex --force` (or `kb_reindex` with `force: true`) when the server reports an incompatible index. This only rebuilds derived SQLite state; source Markdown remains untouched.
