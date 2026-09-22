# Verification

Use this checklist to confirm that LocalVectorSearchMcp is installed, connected, indexed, and returning useful results.

## 1. Verify the executable

```bash
dotnet tool list --global
local-vector-search-mcp --status
```

The global tool list should contain `DimonSmart.LocalVectorSearchMcp`.

## 2. Verify the embedding endpoint

For the default Ollama setup:

```bash
ollama list
```

Confirm that `bge-m3:latest` is installed and that Ollama is running before the first semantic reindex.

## 3. Verify the MCP registration

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

The server should be listed as enabled and connected.

## 4. Verify tool discovery

Ask the client to list the MCP tools. Exactly fourteen tools should be exposed:

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

For `kb_save_image`, inspect discovery metadata and confirm:

```text
_meta["openai/fileParams"] = ["file"]
```

The `file` object requires `download_url` and `file_id`; `mime_type` and `file_name` are optional. `fileName` and `altText` are optional top-level arguments.

## 5. Build and inspect the index

Ask:

```text
Use kb_status to inspect the documentation index.
If no documents are indexed, call kb_reindex.
Poll kb_status until indexing.isRunning is false.
Then report indexing.last.result, the indexed document count, and index path.
```

Alternatively:

```bash
local-vector-search-mcp --reindex
local-vector-search-mcp --status
```

Confirm that the project contains:

```text
.local-vector-search-mcp/index.db
```

## 6. Verify search and focused reading

Choose a distinctive literal phrase and find it in lexical mode. Then ask for the same concept without copying the wording in semantic mode. Finally run hybrid search and open the best result with `kb_read`.

This verifies FTS, embeddings/sqlite-vec, Reciprocal Rank Fusion, semantic pointers, and focused Markdown reads.

## 7. Verify editable Markdown workbench behavior

Use a disposable workspace with:

```yaml
knowledgeBase:
  allowWrites: true
  watchFiles: true
```

Verify:

1. `kb_create` commits Markdown immediately and returns `indexSynchronized=false` while index reconciliation may still be pending; after reconciliation completes, the new content becomes searchable.
2. Concrete pointers returned by read/search/outline use canonical `logical~selfHash~subtreeHash` anchors; each hash is 16 lowercase hex characters and leaf hashes are equal.
3. A heading body edit leaves its `selfHash` unchanged but changes its `subtreeHash`; raw comments or other non-indexed source inside the section also change `subtreeHash`.
4. `replace_element` with an older still-valid heading `selfHash` survives descendant edits, while `replace_section` with the old v2 anchor is rejected. Legacy `logical~selfHash` remains valid for Self operations but is rejected for `replace_section` with a reread message.
5. Structural relocation uses only exact element kind plus `selfHash`; direct logical-pointer match wins over duplicates and ambiguous relocation is rejected.
6. After a mutation, an immediate `kb_read` returns the committed source and matching `sourceHash` even while reconciliation is blocked or failing; `kb_search` may still return the previous revision and each result exposes its `indexedSourceHash`.
7. `kb_move` and `kb_delete` retain their latest-`sourceHash` whole-file contract.
8. `kb_list_files` returns non-Markdown files as `asset`.

## 8. Verify image save

Use a disposable writable workspace and pass a small PNG through ChatGPT's OpenAI file parameter:

```text
Save this image as chapter-01.png with alt text "Chapter 1".
```

Confirm that:

- `kb_save_image` creates `<root>/images/chapter-01.png`;
- returned `mimeType` is `image/png`;
- `bytes` matches the file length;
- `sha256` is lowercase and matches the file;
- the returned Markdown is usable;
- no `.upload-*.tmp` file remains;
- `kb_list_files` reports the file as `asset`.

Save the same name again and confirm the original remains unchanged and the new image receives `-2` (then `-3`, etc.).

Supported save formats are PNG, JPEG, WebP, and GIF. SVG and unknown signatures must be rejected. The hard transfer limit is 25 MiB.

## 9. Verify image list and load

Set `knowledgeBase.allowWrites: false` and confirm both tools still work.

`kb_list_images` should recursively list only supported image extensions beneath `images/`, use stable ordering, and paginate with an opaque cursor. The default page size is 50; valid values are 1..200.

Call:

```text
kb_load_image path=images/chapter-01.png
```

Confirm the result contains a real MCP `ImageContentBlock`, not only JSON/base64 text. Structured metadata should contain `path`, `mimeType`, `bytes`, and `sha256`.

A mismatched extension/signature, file over 25 MiB, traversal path, outside-`images/` path, or symlink/junction/reparse escape must be rejected.

## 10. Verify image delete

With writes disabled, `kb_delete_image` must return a controlled error and leave the file untouched.

With writes enabled, delete a nested image path and confirm:

- only the named file disappears;
- its parent directory is not automatically removed;
- `kb_list_images` no longer returns it;
- `kb_list_files` no longer returns it.

Missing files, directories, unsupported extensions, outside-`images/` paths, and linked/reparse paths must be rejected.

## 11. Verify index isolation

Compare index status/search before and after image save/list/load/delete. Image operations must not:

- create indexed documents;
- create FTS/vector chunks;
- invoke image embeddings;
- change the configured Markdown source set;
- require Markdown index synchronization.

## 12. Verify non-blocking reindex

Use a deliberately slow local embedding endpoint so the embedding request can be held open deterministically.

1. Call `kb_reindex` and confirm the MCP call returns before the embedding request is released.
2. While indexing is still blocked, confirm `kb_status.indexing.isRunning=true`.
3. Confirm `kb_read`, `kb_list_files`, and `kb_outline` return before the slow embedding is released. During a destructive forced rebuild, confirm only `kb_search` returns the controlled rebuild-in-progress error.
4. Call `kb_reindex` again and confirm it returns `started=false` without queuing another pipeline.
5. Release the embedding request and confirm `kb_status.indexing.isRunning=false` and `kb_status.indexing.last.outcome` is populated.

## Developer verification

From the repository root:

```bash
dotnet build
dotnet test
```

Release CI additionally runs formatting, builds, and tests on Linux, Windows, and macOS.
