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
configured Markdown workspace
```

LocalVectorSearchMcp does not implement tunnel authentication, HTTP hosting, OAuth, or tunnel protocol logic. Tunnel credentials belong to `tunnel-client`.

## Prerequisites

- LocalVectorSearchMcp installed and runnable as `local-vector-search-mcp`.
- The embedding provider required by your LocalVectorSearchMcp configuration, if you plan to reindex or run semantic search.
- Access to OpenAI Secure MCP Tunnel and its `tunnel-client` distribution.
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

For this deployment style, use:

- an absolute `--config` path;
- an explicit `knowledgeBase.root`;
- an explicit `storage.path`.

The ordinary configless Claude Code and Codex scenarios remain supported; these recommendations are specific to the tunnel scenario.

## Initialize the tunnel runtime

The following PowerShell example uses a local stdio MCP child process:

```powershell
$env:CONTROL_PLANE_API_KEY = "<runtime-key>"

.\tunnel-client.exe init `
  --sample sample_mcp_stdio_local `
  --profile local-vector-search `
  --tunnel-id tunnel_... `
  --mcp-command "local-vector-search-mcp --config C:/Configs/my-kb.yml"
```

Do not put the runtime key or other secrets inside `--mcp-command`, LocalVectorSearchMcp YAML, source control, shell scripts committed to the repository, or LocalVectorSearchMcp CLI options.

Validate the profile before starting it:

```powershell
.\tunnel-client.exe doctor `
  --profile local-vector-search `
  --explain
```

Then run it:

```powershell
.\tunnel-client.exe run `
  --profile local-vector-search
```

If your `tunnel-client` version uses the managed runtime flow, the equivalent setup may use:

```text
tunnel-client runtimes connect
tunnel-client runtimes status
```

Use the flow exposed by your current OpenAI tunnel setup. LocalVectorSearchMcp itself is unchanged in either case.

## Connect from ChatGPT

After the tunnel runtime reports ready:

1. Add or select the Secure MCP Tunnel in ChatGPT using the tunnel created for this runtime.
2. Refresh or scan the connector so ChatGPT discovers the MCP tools.
3. Verify that the twelve LocalVectorSearchMcp tools are visible.
4. Call `kb_status`.
5. Call `kb_search` and `kb_read` against the configured workspace.

The MCP surface is the same surface used by Claude Code and Codex:

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
debug_receive_file
debug_return_test_image
```

## File and image transfer diagnostics

The two `debug_*` tools exist specifically to isolate ChatGPT/Secure-Tunnel file-transfer behavior before binary asset upload is added to the workspace model.

`debug_receive_file` declares its top-level `file` argument through `_meta["openai/fileParams"]`. ChatGPT should therefore pass the OpenAI file object containing `download_url` and `file_id`, plus optional `mime_type` and `file_name`. The server downloads the temporary HTTPS URL, streams at most 25 MiB, does not save the bytes, and returns byte count, SHA-256, MIME information, and a short hexadecimal prefix.

Use it for three manual checks:

1. Attach a small PNG to the conversation and ask ChatGPT to pass that file to `debug_receive_file`.
2. Generate an image in ChatGPT, then ask ChatGPT to pass the just-generated image to `debug_receive_file`.
3. Select an existing image from ChatGPT's file library, then pass it to `debug_receive_file`.

If the second scenario works, the desired Image Generation → ChatGPT file object → Secure MCP Tunnel → local MCP chain is available without base64 arguments or a custom upload protocol.

`debug_return_test_image` performs the independent reverse-direction check. It returns a tiny valid PNG as standard MCP `ImageContentBlock`. Ask ChatGPT to call it and confirm that an image is received/rendered.

These diagnostics intentionally do not write assets into the configured workspace. They can be removed or replaced by the chosen production asset-import workflow after the experiment.

## Read-only deployment

Read-only is the recommended starting configuration:

```yaml
knowledgeBase:
  allowWrites: false
```

The server still exposes its normal MCP surface, but mutation calls are rejected by the existing application-level write guard. No tunnel-specific scopes or authentication rules are required.

## Writable deployment

Enable writes only for a workspace where ChatGPT should be allowed to modify Markdown:

```yaml
knowledgeBase:
  allowWrites: true
```

The existing `kb_patch`, `kb_create`, `kb_move`, and `kb_delete` behavior is then used without any tunnel-specific implementation. Whether ChatGPT can execute write actions also depends on the capabilities and policy of the ChatGPT product/workspace in which the connector is used.

## One active stdio runtime per tunnel ID

For one tunnel ID that bridges a stdio MCP server, run only one active `tunnel-client` instance. During restart or upgrade, stop the old runtime before starting the new one.

This is a Secure MCP Tunnel runtime constraint, not a LocalVectorSearchMcp multi-process or storage rule.

## Manual verification

Use this sequence after initial setup or after changing tunnel configuration:

1. Install or update the OpenAI-provided `tunnel-client`.
2. Create or select the tunnel.
3. Create a runtime API key.
4. Configure the local stdio command with an absolute LocalVectorSearchMcp config path.
5. Run `tunnel-client doctor --profile local-vector-search --explain`.
6. Start the tunnel runtime.
7. Confirm that the runtime reports ready.
8. Add or select the tunnel in ChatGPT and refresh tool discovery.
9. Confirm that all twelve tools are discovered.
10. Call `kb_status`.
11. Call `kb_search`.
12. Call `kb_read`.
13. Call `debug_return_test_image` and verify that ChatGPT receives a PNG.
14. Pass a small attached PNG to `debug_receive_file` and verify the returned size and SHA-256.

For a workspace intended to be writable, additionally verify both configurations:

```text
allowWrites=false -> mutation is rejected
allowWrites=true  -> mutation succeeds
```

Run the writable check only when the ChatGPT product/workspace permits write MCP actions.

## Troubleshooting

### The runtime starts but the wrong workspace is used

Use an absolute `--config` path and explicit absolute `knowledgeBase.root` and `storage.path`. Do not depend on the current directory inherited from `tunnel-client`.

### MCP initialization or tool discovery fails

Run `tunnel-client doctor --profile local-vector-search --explain`. Also run the same LocalVectorSearchMcp command locally to confirm that it starts as a stdio server.

LocalVectorSearchMcp stdout is reserved for MCP protocol traffic. Diagnostics and logs must go to stderr. Do not add `Console.WriteLine` startup messages or logging providers that write diagnostics to stdout.

### A restarted tunnel behaves inconsistently

Make sure the previous `tunnel-client` instance using the same stdio tunnel ID is stopped before the replacement instance starts.

### Writes are rejected

Check `knowledgeBase.allowWrites`. The default is `false`. Enabling writes is a LocalVectorSearchMcp application decision; tunnel authentication does not replace or override this guard.

### Credentials appear in repository configuration

Remove them from LocalVectorSearchMcp configuration and history. Runtime/tunnel credentials belong to `tunnel-client` or its environment, for example `CONTROL_PLANE_API_KEY`; they are not LocalVectorSearchMcp settings.

## Scope boundary

Secure MCP Tunnel provides remote connectivity to the existing local stdio process. It does not turn LocalVectorSearchMcp into an HTTP MCP server.

Direct remote HTTP MCP hosting, OAuth/OIDC, JWT validation, CORS, reverse proxy configuration, TLS termination, HTTP health endpoints, and a custom tunnel protocol client remain outside this project's current scope.
