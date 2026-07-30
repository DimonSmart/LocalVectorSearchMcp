# Codex setup

LocalVectorSearchMcp runs as a local stdio MCP server. Codex CLI, the Codex IDE extension, and the ChatGPT desktop app share MCP configuration for the same Codex host.

## Basic registration

Run:

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp
```

Codex stores CLI-added MCP configuration in `~/.codex/config.toml` by default. Start Codex from the project root so the server process inherits the correct working directory.

LocalVectorSearchMcp uses the current working directory as the project root when `CLAUDE_PROJECT_DIR` is not present.

## Registration with server options

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp \
    --root docs \
    --embedding-endpoint http://localhost:11434/v1 \
    --embedding-model bge-m3:latest
```

Exclude generated or vendored documentation explicitly:

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp \
    --exclude "**/node_modules/**" \
    --exclude "**/.git/**"
```

`--include` and `--exclude` are repeatable. Supplying either list on the command line replaces the corresponding YAML or default list.

## Registration with YAML

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp \
    --config local-vector-search-mcp.yml
```

See [Configuration](../configuration.md) for the YAML schema and precedence rules.

## Project-scoped Codex configuration

Trusted projects can keep MCP configuration in `.codex/config.toml`.

```toml
[mcp_servers.local-vector-search]
command = "local-vector-search-mcp"
args = []
```

With server arguments:

```toml
[mcp_servers.local-vector-search]
command = "local-vector-search-mcp"
args = [
  "--root", "docs",
  "--embedding-endpoint", "http://localhost:11434/v1",
  "--embedding-model", "bge-m3:latest"
]
```

A project-scoped configuration is useful when the repository should describe its own MCP setup. Keep machine-specific absolute paths and secrets out of committed configuration.

## Verify the registration

```bash
codex mcp list
codex mcp get local-vector-search
```

Inside the Codex terminal UI, use:

```text
/mcp
```

Then ask:

```text
Use kb_status to inspect the documentation index.
If it is missing, call kb_reindex and report how many documents were indexed.
```

## Remove the registration

For a server added through the CLI:

```bash
codex mcp remove local-vector-search
```

For a project-scoped server, remove the `[mcp_servers.local-vector-search]` table from `.codex/config.toml`.

Removing the MCP registration does not delete the local SQLite index. Delete `.local-vector-search-mcp/` separately when the cached index is no longer needed.

## Equivalent Claude Code examples

The corresponding Claude Code setup is documented in [Claude Code setup](claude-code.md). Every server-argument example has the same argument list after the MCP client separator.
