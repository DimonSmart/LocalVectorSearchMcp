# ChatGPT via OpenAI Secure MCP Tunnel

LocalVectorSearchMcp is a local MCP server over stdio. To use the same server from ChatGPT without exposing a local HTTP listener, run it behind the OpenAI Secure MCP Tunnel. The tunnel is external to LocalVectorSearchMcp and launches the existing stdio server as a child process.

```text
ChatGPT
    ↓
OpenAI tunnel service
    ↓
tunnel-client
    ↓ stdio
local-vector-search-mcp
    ↓
configured Markdown workspace + images/
```

LocalVectorSearchMcp does not implement tunnel authentication, HTTP hosting, OAuth, or tunnel protocol logic. Tunnel credentials belong to `tunnel-client`.

## Prerequisites

- LocalVectorSearchMcp installed and runnable as `local-vector-search-mcp`.
- The embedding provider required by your configuration if you plan to reindex or run semantic search.
- Access to OpenAI Secure MCP Tunnel and its current `tunnel-client` distribution.
- A tunnel ID and runtime API key created through the OpenAI tunnel setup flow.

Use the `tunnel-client` version and distribution provided by OpenAI for your account/workspace. The tunnel CLI can evolve independently of this project.

## Configure the knowledge base explicitly

A tunnel runtime may start the child process from a directory unrelated to the knowledge base. Do not rely on `CurrentDirectory`, `CLAUDE_PROJECT_DIR`, or the directory from which `tunnel-client` happened to be started.

Create a configuration with explicit absolute paths:

```yaml
knowledgeBase:
  root: C:/Projects/MyKnowledgeBase
  allowWrites: false
  watchFiles: true

storage:
  path: C:/Projects/MyKnowledgeBase/.local-vector-search-mcp/index.db
```

For this deployment style, use an absolute `--config` path, explicit `knowledgeBase.root`, and explicit `storage.path`.

## Initialize the tunnel runtime

A PowerShell setup for a local stdio child process may look like:

```powershell
$env:CONTROL_PLANE_API_KEY = "<runtime-key>"

.\tunnel-client.exe init `
  --sample sample_mcp_stdio_local `
  --profile local-vector-search `
  --tunnel-id tunnel_... `
  --mcp-command "local-vector-search-mcp --config C:/Configs/my-kb.yml"
```

Do not put the runtime key or other secrets inside `--mcp-command`, LocalVectorSearchMcp YAML, source control, or LocalVectorSearchMcp CLI options.

Validate and run the profile using the commands exposed by your current OpenAI tunnel distribution, for example:

```powershell
.\tunnel-client.exe doctor --profile local-vector-search --explain
.\tunnel-client.exe run --profile local-vector-search
```

## Connect from ChatGPT

After the tunnel runtime reports ready:

1. Add or select the Secure MCP Tunnel in ChatGPT.
2. Refresh tool discovery.
3. Confirm that all fourteen LocalVectorSearchMcp tools are visible.
4. Call `kb_status`.
5. Call `kb_search` and `kb_read` against the configured workspace.

The surface is:

```text
kb_status
kb_reindex
kb_search
kb_read
kb_patch
kb_create
kb_move
kb_delete
kb_list_files
kb_outline
kb_save_image
kb_list_images
kb_load_image
kb_delete_image
```

## Image transfer and storage

### ChatGPT → workspace

`kb_save_image` declares its top-level `file` argument through:

```text
_meta["openai/fileParams"] = ["file"]
```

ChatGPT passes the OpenAI file object containing `download_url` and `file_id`, with optional `mime_type` and `file_name`. The server downloads the temporary URL over HTTPS, enforces a 25 MiB hard limit, detects the real image format from its signature, computes SHA-256, and stores it in:

```text
<knowledgeBase.root>/images/
```

Supported formats are PNG, JPEG, WebP, and GIF.

The temporary URL is security-sensitive. LocalVectorSearchMcp rejects non-HTTPS URLs, URL user-info, loopback/private/link-local destinations, and unsafe redirect targets. It does not log the complete signed URL or query string.

Saving requires:

```yaml
knowledgeBase:
  allowWrites: true
```

Example request intent:

```text
Save this attached image as chapter-10-01.png with alt text
"Diagram for chapter 10".
```

A successful response includes a Markdown snippet such as:

```markdown
![Diagram for chapter 10](images/chapter-10-01.png)
```

Existing files are not overwritten. A collision produces `chapter-10-01-2.png`, then `-3`, and so on.

### Workspace → ChatGPT

`kb_list_images` recursively lists supported image assets beneath `images/` with cursor pagination. It is read-only.

`kb_load_image("images/chapter-10-01.png")` revalidates size and signature, computes SHA-256, and returns the actual image as standard MCP `ImageContentBlock` plus structured metadata. ChatGPT can therefore inspect or analyze an existing workspace image without a custom base64 protocol.

Both list and load work when `knowledgeBase.allowWrites=false`.

### Delete

`kb_delete_image` deletes exactly one supported image file beneath `images/` and requires writes to be enabled. Nested paths created by external tools are supported. Parent directories are not removed automatically.

Images remain ordinary assets. They are visible in `kb_list_files` but are not indexed, embedded, or added to FTS/vector search.

## Read-only deployment

Read-only is the recommended starting configuration:

```yaml
knowledgeBase:
  allowWrites: false
```

Search/read/list operations, including `kb_list_images` and `kb_load_image`, remain available. Markdown mutations plus image save/delete are rejected by the application write guard.

## Writable deployment

Enable writes only for a workspace where ChatGPT should be allowed to modify sources/assets:

```yaml
knowledgeBase:
  allowWrites: true
```

Whether ChatGPT can execute write actions also depends on the capabilities and policy of the ChatGPT product/workspace in which the connector is used.

## One active stdio runtime per tunnel ID

For one tunnel ID that bridges a stdio MCP server, run only one active `tunnel-client` instance. During restart or upgrade, stop the old runtime before starting the new one.

This is a Secure MCP Tunnel runtime constraint, not a LocalVectorSearchMcp storage rule.

## Manual verification

After setup:

1. Start the configured LocalVectorSearchMcp stdio command locally and verify it starts cleanly.
2. Run tunnel doctor and start the tunnel runtime.
3. Connect ChatGPT and refresh tool discovery.
4. Confirm exactly fourteen tools.
5. Call `kb_status`, `kb_search`, and `kb_read`.
6. Call `kb_list_images`.
7. Call `kb_load_image` for an existing PNG and confirm ChatGPT receives an image.
8. In a disposable writable workspace, attach a small PNG and call `kb_save_image`; verify the file, SHA-256, and Markdown response.
9. Save the same name twice and confirm no overwrite.
10. Call `kb_delete_image` and confirm only the target asset disappears.
11. Repeat list/load with writes disabled and confirm they remain available.

## Troubleshooting

### The runtime starts but the wrong workspace is used

Use an absolute `--config` path and explicit absolute `knowledgeBase.root` and `storage.path`. Do not depend on the current directory inherited from `tunnel-client`.

### MCP initialization or tool discovery fails

Run the diagnostic command provided by your current `tunnel-client` distribution and also run the same LocalVectorSearchMcp command locally.

LocalVectorSearchMcp stdout is reserved for MCP protocol traffic. Diagnostics and logs must go to stderr.

### Image save is rejected

Check all of the following:

- `knowledgeBase.allowWrites=true`;
- the source is PNG/JPEG/WebP/GIF and not over 25 MiB;
- explicit `fileName` is a basename, not a path;
- the extension agrees with the actual image signature.

### Image list/load works but save/delete does not

This is expected when `allowWrites=false`. List/load are read-only; save/delete are mutations.

### Credentials appear in repository configuration

Remove them from LocalVectorSearchMcp configuration and history. Runtime/tunnel credentials belong to `tunnel-client` or its environment. OpenAI signed file download URLs are transient input and must not be copied into logs or configuration.

## Scope boundary

Secure MCP Tunnel provides remote connectivity to the existing local stdio process. It does not turn LocalVectorSearchMcp into an HTTP MCP server.

Direct remote HTTP MCP hosting, OAuth/OIDC, JWT validation, CORS, reverse proxy configuration, TLS termination, HTTP health endpoints, and a custom tunnel protocol client remain outside this project's scope.
