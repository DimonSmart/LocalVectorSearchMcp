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
If it is missing, call kb_reindex and report how many documents were indexed.
```

## Remove the registration

```bash
claude mcp remove local-vector-search
```

Removing the MCP registration does not delete the local SQLite index. Delete `.local-vector-search-mcp/` separately when the cached index is no longer needed.

## Equivalent Codex examples

The corresponding Codex setup is documented in [Codex setup](codex.md). Every server-argument example has the same argument list after the MCP client separator.
