# Getting started

This guide installs LocalVectorSearchMcp, connects it to Claude Code or Codex, builds the first index, and verifies that search works.

## Prerequisites

- .NET 10 SDK or runtime capable of running .NET global tools.
- [Ollama](https://ollama.com/) or another OpenAI-compatible embedding endpoint.
- Claude Code, Codex, or both.
- A project containing Markdown documentation.

The default configuration uses:

```text
Embedding endpoint: http://localhost:11434/v1
Embedding model:    bge-m3:latest
Indexed files:      **/*.md
Index path:         .local-vector-search-mcp/index.db
Search mode:        hybrid
```

## 1. Install LocalVectorSearchMcp

```bash
dotnet tool install --global DimonSmart.LocalVectorSearchMcp
```

Confirm that the command is available:

```bash
local-vector-search-mcp --status
```

Running `--status` outside the intended project is harmless, but the reported paths will be based on that current directory.

## 2. Prepare Ollama

```bash
ollama pull bge-m3:latest
ollama serve
```

Skip `ollama serve` when Ollama is already running as a service or desktop application.

The embedding endpoint must be reachable when the first index is built.

## 3. Connect an MCP client

Run the registration command from the project root.

### Claude Code

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp
```

### Codex

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp
```

Detailed client-specific setup:

- [Claude Code](clients/claude-code.md)
- [Codex](clients/codex.md)

## 4. Build the first index

The preferred workflow is to let the agent call the MCP tools:

```text
Use kb_status to inspect the local documentation index.
If it is empty or missing, run kb_reindex.
Then search for the project's main architectural decisions.
```

You can also build the index directly from the project root:

```bash
local-vector-search-mcp --reindex
```

The generated database is stored at:

```text
<project-root>/.local-vector-search-mcp/index.db
```

Add `.local-vector-search-mcp/` to `.gitignore`; the index is local runtime data and should not be committed.

## 5. Verify exact and semantic search

First ask for a term that appears literally in your Markdown:

```text
Search the project documentation for "Reciprocal Rank Fusion"
and show the most relevant source.
```

Then ask the same idea without using the exact phrase:

```text
Find the documentation that explains how lexical and vector
result rankings are combined.
```

The first request validates exact retrieval. The second validates semantic or hybrid retrieval.

See [Verification](verification.md) for a complete smoke-test checklist.

## 6. Configure a different documentation root

For a `docs` folder, register the server with `--root docs`.

### Claude Code

```bash
claude mcp add local-vector-search \
  --scope local \
  --transport stdio \
  -- local-vector-search-mcp \
    --root docs
```

### Codex

```bash
codex mcp add local-vector-search \
  -- local-vector-search-mcp \
    --root docs
```

See [Configuration](configuration.md) for embedding, include/exclude, storage, chunking, and search options.

## Next steps

- [Configure Claude Code](clients/claude-code.md)
- [Configure Codex](clients/codex.md)
- [Customize indexing and embeddings](configuration.md)
- [Update or rebuild the tool and index](maintenance.md)
- [Diagnose startup and search problems](troubleshooting.md)
