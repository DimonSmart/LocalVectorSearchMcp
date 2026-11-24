# Using LocalVectorSearchMcp with Intent-Driven Development

LocalVectorSearchMcp remains a general-purpose Markdown MCP server. IDD may configure it, but the server has no dependency on IDD and contains no IDD-specific search or duplicate-detection logic.

For IDD, use a read-only knowledge base rooted at `.idd/intent`; SQLite is derived state and lives outside that directory. Ollama and vector search are optional.

## Requirements

Choose one execution form:

- .NET 10 SDK for `dnx`, global tools, or project-local tools; or
- a self-contained executable from the matching GitHub Release.

Ollama is required only for semantic/hybrid search. Lexical search works without Ollama, sqlite-vec initialization, or network access.

Always pin a package version that is actually published. In commands below, replace `VERSION` with the same version visible on NuGet and the corresponding GitHub Release. Do not assume a version mentioned in an older document is published.

## Recommended IDD layout

```text
project/
├── .idd/
│   ├── intent/
│   │   ├── INDEX.md
│   │   ├── IDD-0001.spec-example.md
│   │   └── IDD-0002.adr-example.md
│   └── local-vector-search-mcp.yml
└── .local-vector-search-mcp/
    └── index.db
```

Add this to `.gitignore`:

```gitignore
.local-vector-search-mcp/
```

Do not ignore `.idd/local-vector-search-mcp.yml` when the project intentionally shares the MCP configuration.

## Recommended read-only hybrid configuration

Create `.idd/local-vector-search-mcp.yml`:

```yaml
paths:
  relativeTo: config

storage:
  path: ../.local-vector-search-mcp/index.db

embedding:
  provider: openai-compatible
  endpoint: http://localhost:11434/v1
  apiKey: ollama
  model: bge-m3:latest

search:
  defaultMode: hybrid

knowledgeBase:
  root: intent
  allowWrites: false
  watchFiles: true
  include:
    - "IDD-*.md"
  exclude:
    - "**/*.cosmic.md"
```

Because `paths.relativeTo: config` is explicit, `knowledgeBase.root: intent` resolves to `.idd/intent` and the storage path resolves to `.local-vector-search-mcp/index.db`, regardless of the MCP client's current working directory once `--project-root` and `--config` identify the project.

The include example intentionally selects direct `IDD-*.md` files under the intent root. If a project uses nested intent documents or another naming convention, use `**/*.md` or the project's actual glob instead. The server uses filesystem glob semantics; it does not interpret IDD document types.

## Lexical-only configuration without Ollama

For machines where embeddings are unwanted or unavailable, replace the embedding and search sections with:

```yaml
embedding:
  provider: none

search:
  defaultMode: lexical
```

In this mode indexing, watching, reindexing, and lexical search do not call an embedding provider. Explicit semantic search returns a controlled disabled-capability error. Hybrid requests fall back to lexical with an explicit warning.

## Remote Ollama

For Ollama on another machine, use its OpenAI-compatible endpoint and explicitly allow a remote endpoint:

```yaml
embedding:
  provider: openai-compatible
  endpoint: http://192.168.1.20:11434/v1
  apiKey: ollama
  model: bge-m3:latest
  allowRemoteEndpoint: true
```

The model must support embeddings. Changing the embedding model or dimensions makes an existing vector index incompatible and requires a forced rebuild.

## Verify the published package with dnx

From any directory:

```bash
dnx DimonSmart.LocalVectorSearchMcp@VERSION --yes -- \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml \
  --status
```

Build the initial index with:

```bash
dnx DimonSmart.LocalVectorSearchMcp@VERSION --yes -- \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml \
  --reindex
```

The release workflow smoke-tests the same config-relative `.idd` layout using both a project-local .NET tool and `dnx` before publishing the NuGet package.

## Global and project-local .NET tools

Global installation:

```bash
dotnet tool install --global DimonSmart.LocalVectorSearchMcp --version VERSION
local-vector-search-mcp \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml \
  --status
```

Project-local installation must be run from the directory whose .NET tool manifest should own the tool:

```bash
dotnet new tool-manifest # only when .config/dotnet-tools.json does not exist
dotnet tool install DimonSmart.LocalVectorSearchMcp --version VERSION
dotnet tool run local-vector-search-mcp -- \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml \
  --status
```

`--project-root` does not change how the .NET CLI discovers a local tool manifest. Use `dnx` or a self-contained executable when the MCP client's working directory makes manifest lookup unreliable.

## Codex registration

```bash
codex mcp add local-vector-search -- \
  dnx DimonSmart.LocalVectorSearchMcp@VERSION --yes -- \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml
```

Verify with `codex mcp list`, `codex mcp get local-vector-search`, and then call `kb_status` from the project.

For clients that can restrict MCP tools, prefer a client-side allowlist for the IDD workflow rather than a server-side IDD mode. The useful read-only subset is `kb_search`, `kb_read`, `kb_outline`, `kb_links`, `kb_list_files`, `kb_status`, and `kb_reindex`.

## Claude Code registration

```bash
claude mcp add local-vector-search \
  --scope local --transport stdio -- \
  dnx DimonSmart.LocalVectorSearchMcp@VERSION --yes -- \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml
```

Verify with `claude mcp list`, `claude mcp get local-vector-search`, and then call `kb_status`.

## Read-only behavior

With `allowWrites: false`, search, reads, outlines, links, file listing, status, reindexing, watcher synchronization, and derived SQLite updates remain available. MCP operations that mutate Markdown or workspace assets are rejected. Source path access remains constrained to the configured knowledge-base root; traversal and unsafe link/reparse-point escapes are rejected.

The server writes its SQLite index even when source writes are disabled. That index is disposable derived state.

## Index freshness

With `watchFiles: true`:

- external Markdown create/edit/delete/rename events are debounced and reconciled;
- rapid repeated changes are coalesced;
- on server startup a non-blocking changed-scope reconciliation discovers changes made while the server was stopped, including Git checkout/pull/reset effects that are visible in the final filesystem state;
- a missing index is built by the same startup reconciliation;
- `kb_read` always reads current source, while `kb_search` may briefly return an older indexed revision; compare `sourceHash` with `indexedSourceHash` when freshness matters.

If synchronization or embeddings fail, source Markdown remains authoritative. Inspect `kb_status` for pending synchronization, reindex state, compatibility, and safe diagnostics.

## Rebuild and recovery

Normal changed-file reconciliation:

```bash
local-vector-search-mcp \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml \
  --reindex
```

Forced rebuild after an incompatible schema/index mode/embedding/chunking change:

```bash
local-vector-search-mcp \
  --project-root /absolute/path/to/project \
  --config .idd/local-vector-search-mcp.yml \
  --reindex --force
```

To recover from a damaged derived index, stop active MCP processes, delete `.local-vector-search-mcp/`, and reindex. Do not modify Markdown files to repair SQLite.

## Known operational limits

- One MCP process should be configured per project root. Separate projects should use separate storage paths.
- Concurrent clients sharing one project also share the same SQLite file; verify the intended Codex + Claude Code concurrency on the target machines before making it a required workflow.
- Real Ollama behavior, cross-language embedding quality, client-specific tool allowlists, and large-corpus performance depend on the local model/client/environment and should be smoke-tested there rather than inferred from deterministic CI.
- Diagnostic logging is written to stderr so stdio MCP protocol messages on stdout remain clean.
