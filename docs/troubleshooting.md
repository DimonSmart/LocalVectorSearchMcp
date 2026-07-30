# Troubleshooting

## The MCP server is not visible

Verify that the client registration exists.

### Claude Code

```bash
claude mcp list
claude mcp get local-vector-search
```

### Codex

```bash
codex mcp list
codex mcp get local-vector-search
```

Also confirm that this command succeeds:

```bash
local-vector-search-mcp --status
```

Restart the MCP client after changing registration or updating the global tool.

## The executable cannot be found

Check global tools:

```bash
dotnet tool list --global
```

Reinstall when necessary:

```bash
dotnet tool install --global DimonSmart.LocalVectorSearchMcp
```

Make sure the .NET global-tools directory is on `PATH`.

## The embedding endpoint is unavailable

For the default Ollama configuration:

```bash
ollama list
ollama serve
```

Confirm that the configured model is installed:

```bash
ollama pull bge-m3:latest
```

If Ollama already runs as a desktop application or service, do not start a second server process.

For a custom endpoint, verify `--embedding-endpoint` or the YAML `embedding.endpoint`.

## The wrong project is being indexed

Run:

```bash
local-vector-search-mcp --status
```

Inspect the detected project root and knowledge-base root.

Claude Code supplies `CLAUDE_PROJECT_DIR`. Codex and direct CLI execution use the server process working directory unless `--root` or YAML overrides the knowledge-base root.

Start the client from the intended project root, or configure an explicit root:

```text
--root docs
```

## The index is empty

Confirm that:

- the configured root exists;
- Markdown files match the include patterns;
- exclude patterns do not remove all files;
- the embedding endpoint is reachable;
- reindex completed successfully.

Run:

```bash
local-vector-search-mcp --reindex
local-vector-search-mcp --status
```

The default include pattern is `**/*.md`. The default exclude list is empty.

## Search returns no useful results

Try the modes separately:

```text
lexical
semantic
hybrid
```

Use lexical search for exact identifiers and quoted terms. Use semantic search for conceptual matches. Hybrid is the general default.

Also verify that the relevant file was indexed and that the requested `topK` is not too small.

## The index is incompatible after configuration changes

Changing embedding or chunking settings may require a forced rebuild:

```bash
local-vector-search-mcp --reindex --force
```

The source Markdown files are not modified.

## A remote embedding endpoint is rejected

Remote endpoints are disabled by default. Explicitly allow one in YAML:

```yaml
embedding:
  endpoint: https://example.com/v1
  allowRemoteEndpoint: true
```

Only enable this when sending project documentation to that endpoint is acceptable.

## Claude Code uses the wrong scope

Inspect:

```bash
claude mcp get local-vector-search
```

Available scopes are:

- `local` — private to you in the current project;
- `project` — shared through `.mcp.json`;
- `user` — available to you in all projects.

Remove and re-add the registration with the intended `--scope`.

## Codex uses an unexpected configuration

Codex reads `~/.codex/config.toml` and, for trusted projects, project-scoped `.codex/config.toml`.

Inspect:

```bash
codex mcp get local-vector-search
```

Check both configuration files for duplicate `[mcp_servers.local-vector-search]` entries or different argument lists.

## Reset a damaged local index

Stop the MCP client, delete the configured SQLite index, and rebuild:

```text
<project-root>/.local-vector-search-mcp/
```

Then run:

```bash
local-vector-search-mcp --reindex
```

## Logs corrupt MCP communication

In MCP mode, standard output is reserved for MCP transport and application logs are written to standard error. When running custom wrappers, do not redirect diagnostic text into the server's standard output.
