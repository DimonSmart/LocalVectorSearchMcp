# MCP contract audit

## Scope and method

Audited revision: current working tree based on `8101af7ffdd3f17b562577fd124148c89e3ac779`.
The server uses `ModelContextProtocol` `2.0.0-preview.1` over stdio. The audit
uses the real MCP client and transport in `StdioTransportIntegrationTests`, not
reflection of tool attributes alone: it calls `initialize`, `tools/list`, and
safe `tools/call` requests, resolves every local `$ref`, and validates returned
`structuredContent` against the published output schema.

The SDK generates both schemas from the public C# DTOs. It uses `$defs` and
local `$ref` for nested and recursive models. This is valid JSON Schema and is
deliberately retained; flattening the outline would lose its tree semantics.

## Contract matrix

| Tool | Input/result contract | Finding |
| --- | --- | --- |
| `kb_reindex` | typed request and async status result | Concrete schemas; scope enum is published. |
| `kb_status` | typed nested status result | Concrete `project`, `synchronization`, `indexing`, and `compatibility` schemas. Terminal outcome is now a closed enum. |
| `kb_search` | typed request and search result | Modes and result fields are concrete enums/objects. |
| `kb_read` | typed slice with nullable table | Table columns, rows, cells, and alignment are concrete; alignment is now a closed enum. |
| `kb_outline` | recursive heading tree | `OutlineNode.children` is a valid local recursive reference. |
| `kb_patch` | typed operation request/result | Operation kind enum and response schema are published. |
| `kb_edit_table` | flat typed arguments/result | Flat shape is intentional for the existing MCP tool; per-action requirements are described and enforced by the server. |
| `kb_create` | typed request/result | Concrete schema and structured result. |
| `kb_move` | typed request/result | Concrete schema, including source-hash guard. |
| `kb_delete` | typed request/result | Concrete schema, including source-hash guard. |
| `kb_list_files` | typed filter/result | File kind is a closed enum. |
| `kb_create_pdf_book` | typed export request/result | Page format and style are closed enums; the response is structured. |
| `kb_download_file` | binary embedded resource plus JSON metadata text | Deliberately no JSON output schema; this is an MCP binary-resource result. |
| `kb_save_image` | OpenAI file parameter/result | File metadata schema and `openai/fileParams` metadata are published. |
| `kb_list_images` | typed paging/result | Concrete cursor and image metadata schema. |
| `kb_load_image` | image block plus JSON metadata text | Deliberately no JSON output schema; this is an MCP multimodal result. |
| `kb_move_image` | typed request/result | Concrete path/SHA request and result schema. |
| `kb_delete_image` | typed request/result | Concrete deleted-result schema. |

## Findings and decisions

### `kb_outline`

The server already publishes `MarkdownOutline` as `outputSchema` and returns
matching `structuredContent`. Its recursive `children` property resolves through
a local `$ref`; the transport regression test exercises three levels. A client
showing this valid recursive schema as `unknown` loses information in its own
schema-to-type projection. No server-side flattening is justified.

### `kb_read.table`

The output DTO was concrete but `MarkdownTableColumn.alignment` was a free
string. It is now the existing `TableAlignment` enum, so `none`, `left`,
`center`, and `right` are published explicitly. `table` remains nullable and
the transport test validates a real addressed table result. Cell values are
normalized display text: inline formatting is removed and escaped pipes become
literal pipes. The `markdown` slice is the source of original markup.

### `kb_status`

All nested status DTOs were already published through the output schema. The
only confirmed missing constraint was terminal reindex outcome; it is now the
closed `ReindexOutcome` enum (`succeeded`, `failed`, `cancelled`). `indexMode`
and `embeddingProvider` remain strings because configuration can extend them.

### `kb_edit_table`

The SDK represents method parameters as one object, so a discriminated union
cannot be inferred from the flat signature. The public request shape is kept
for compatibility. The tool description now specifies the required selectors
and fields for each action; server validation rejects irrelevant or conflicting
fields. No client-specific custom schema was introduced.

### Image tools

`kb_save_image` requires a client-supported OpenAI file reference with
`download_url` and `file_id`. Base64 strings and data URIs are not a supported
fallback. The stdio transport test confirms that missing server-received
metadata reaches the tool and returns controlled `INVALID_ARGUMENT`; the direct
server-layer test confirms the image service is not invoked. A string, Base64,
or data URI rejected while a client adapts tool arguments never enters
`SaveImageAsync`, so a `catch` in that method cannot improve that client-side
message. The input schema and `openai/fileParams` metadata are published and
the supported hand-off is documented as upload/select an image in the client.

`kb_load_image` correctly returns an image content block followed by JSON
metadata; a client may reasonably show the overall multimodal result as
`unknown`. `kb_download_file` similarly returns an embedded binary resource
followed by metadata text. Neither operation has a JSON output schema because
such a schema would misleadingly describe only the metadata and not the
primary binary content.

### `kb_list_files`

`pathPrefix` is intentionally a root-relative directory path or an exact file
path: it uses path-segment matching, so `books` does not also match
`books-old`, and `books/chapter` does not imply `books/chapter.md`. During the
audit, a trailing directory separator was found to be normalized after path
validation, which incorrectly rejected `books/`; normalization now occurs
before validation. The input schema, tool description, README examples, and
navigation regression tests describe and verify the behavior. `includeGlob` is
the filename-pattern mechanism; when present with `pathPrefix`, both filters
apply.

## Regression coverage

`PublishedMcpSchemasDescribeEveryToolAndMatchStructuredResults` verifies all
18 tools through stdio, resolves every local reference in input and output
schemas, checks required enum paths, validates structured responses, exercises
the recursive outline, and reads a real formatted table. It also confirms that
`kb_load_image` and `kb_download_file` have no output schema by design. An
optional `MCP_TOOLS_LIST_SNAPSHOT` path makes that test write the actual
`tools/list` payload for contract comparisons without a hand-maintained
snapshot.
