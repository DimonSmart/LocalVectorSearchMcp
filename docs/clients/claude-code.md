# Claude Code setup

LocalVectorSearchMcp runs as a local stdio MCP server. Claude Code starts one server process for the current project.

## Basic registration

Run from the project root:

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp
```

Claude Code supplies `CLAUDE_PROJECT_DIR`; LocalVectorSearchMcp uses it as the project root when it is present.

## Offline lexical-only, without a globally installed tool

With a .NET 10 SDK, use the one-shot NuGet runner, pinned to a published
version (replace `0.2.0` with the version you actually install):

```bash
claude mcp add local-vector-search \
  --scope local --transport stdio -- \
  dnx DimonSmart.LocalVectorSearchMcp@0.2.0 --yes -- \
  --project-root /absolute/path/to/project \
  --root .idd/intent \
  --embedding-provider none \
  --search-mode lexical \
  --watch-files
```

No Ollama endpoint or embeddings are needed. `--project-root` overrides
`CLAUDE_PROJECT_DIR` and the server working directory. Make sure
`.idd/intent` exists before launching. The configured knowledge-base remains
read-only unless YAML explicitly enables writes.

A self-contained executable from [GitHub Releases](https://github.com/DimonSmart/LocalVectorSearchMcp/releases)
can be substituted for `dnx`, without requiring a .NET SDK or runtime.

## Registration with server options

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --root docs \
    --embedding-endpoint http://localhost:11434/v1 \
    --embedding-model bge-m3:latest
```

Exclude generated or vendored documentation explicitly:

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --exclude "**/node_modules/**" \
    --exclude "**/.git/**"
```

`--include` and `--exclude` are repeatable. Supplying either list on the command line replaces the corresponding YAML or default list.

## Registration with YAML

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --config local-vector-search-mcp.yml
```

See [Configuration](../configuration.md) for the YAML schema and precedence rules.

## Choose the Claude Code scope

Claude Code supports three MCP scopes:

- `local` — private configuration for you in the current project.
- `project` — shared project configuration written to `.mcp.json`.
- `user` — private configuration available in all projects.

For LocalVectorSearchMcp, `local` is a safe default because the server derives its project root from the active Claude Code project.

To share the MCP registration with the repository:

```bash
claude mcp add local-vector-search \
  --scope project \
  --transport stdio \
  -- local-vector-search-mcp
```

Review `.mcp.json` before committing it. Do not place secrets directly in shared configuration.

## Verify the registration

```bash
claude mcp list
claude mcp get local-vector-search
```

Inside Claude Code, use:

```text
/mcp
```

Then ask:

```text
Use kb_status to inspect the documentation index.
If it is missing, call kb_reindex, poll kb_status until indexing.isRunning is false, and report kb_status.indexing.last.result.
```

## Remove the registration

```bash
claude mcp remove local-vector-search
```

Removing the MCP registration does not delete the local SQLite index. Delete `.local-vector-search-mcp/` separately when the cached index is no longer needed.

## Equivalent Codex examples

The corresponding Codex setup is documented in [Codex setup](codex.md). Every server-argument example has the same argument list after the MCP client separator.
