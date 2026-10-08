# Maintenance and updates

This page covers updating the .NET tool, inspecting and rebuilding indexes, and removing local data.

## Update the installed tool

```bash
dotnet tool update --global DimonSmart.LocalVectorSearchMcp
```

Check the installed global tools:

```bash
dotnet tool list --global
```

Restart Claude Code or Codex after updating so the MCP server is started from the new executable.

## Inspect the current index

Run from the project root:

```bash
local-vector-search-mcp --status
```

With an explicit YAML file:

```bash
local-vector-search-mcp \
  --config ./local-vector-search-mcp.yml \
  --status
```

The same status is available to agents through `kb_status`. Inspect `indexMode` (`lexical` or `vector-enabled`), `embeddingProvider`, `compatibility`, document/chunk counts, synchronization, and the last reindex operation.

## Incremental reindex

```bash
local-vector-search-mcp --reindex
```

With YAML:

```bash
local-vector-search-mcp \
  --config ./local-vector-search-mcp.yml \
  --reindex
```

`local-vector-search-mcp --reindex` is synchronous: it waits for the final `ReindexResponse` and then exits.

The MCP `kb_reindex` tool uses a different lifecycle. It starts the single process-local reindex operation and returns immediately with `started: true`, or `started: false` when another reindex is already active. Poll `kb_status.indexing.isRunning`; after it becomes `false`, the final result or error is available in `kb_status.indexing.last`.

## Forced rebuild

```bash
local-vector-search-mcp --reindex --force
```

Use a forced rebuild after changing:

- embedding provider/index mode (`none` versus `openai-compatible`);
- embedding model or dimensions;
- chunking settings;
- an index format or compatibility-sensitive implementation version;
- configuration when the server reports that the existing index is incompatible.

A forced rebuild recreates derived index data. It does not modify source Markdown files. During the destructive reset window, `kb_status`, `kb_read`, `kb_list_files`, and `kb_outline` remain available because they do not require the search index. `kb_search` returns a controlled rebuild-in-progress error instead of waiting for the rebuild.

## Restore and run a project-local tool

From the directory containing the project's tool manifest:

```bash
dotnet new tool-manifest # only if .config/dotnet-tools.json does not exist
dotnet tool install DimonSmart.LocalVectorSearchMcp
dotnet tool restore
dotnet tool run local-vector-search-mcp -- --embedding-provider none --search-mode lexical --status
```

The `dotnet tool` commands require the .NET SDK. Tool manifest lookup is
controlled by the CLI working directory, not by `--project-root`.
For one-shot execution with the .NET 10 SDK, use
`dnx DimonSmart.LocalVectorSearchMcp@<published-version> --yes -- --status`.

## Delete a local index

Stop any active MCP server process, then delete:

```text
<project-root>/.local-vector-search-mcp/
```

The next reindex recreates the database.

When `storage.path` points elsewhere, delete the configured database and its SQLite sidecar files instead.

## Uninstall the tool

```bash
dotnet tool uninstall --global DimonSmart.LocalVectorSearchMcp
```

Uninstalling the executable does not remove MCP client registrations or project index files.

Remove the client registration separately:

### Claude Code

```bash
claude mcp remove local-vector-search
```

### Codex

```bash
codex mcp remove local-vector-search
```

Then remove `.local-vector-search-mcp/` from projects whose cached indexes are no longer needed.

## Run maintenance commands from source

```bash
dotnet run \
  --project src/DimonSmart.LocalVectorSearchMcp.Server \
  -- --status
```

```bash
dotnet run \
  --project src/DimonSmart.LocalVectorSearchMcp.Server \
  -- --reindex
```
