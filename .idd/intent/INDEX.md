# IDD Intent Index

This index helps humans and CodingAgents find relevant current intent documents.
It is not the source of truth.

Current numbered documents directly under `.idd/intent/` contain normative product
intent, ADRs, or active spikes.

Git history is the source for deleted or previous document versions.

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| 0001.spec-product-overview.md | spec | Product overview | Overall MVP scope, solution shape, and non-goals | |
| 0002.spec-configuration-and-security.md | spec | Configuration and security | YAML configuration, validation, endpoint safety, and path access rules | |
| 0003.spec-markdown-model.md | spec | Markdown model | Markdown normalization, semantic pointers, elements, chunks, and embedding text | |
| 0004.spec-index-storage-and-search.md | spec | Index, storage, and search | SQLite, FTS5, sqlite-vec, reindexing, embeddings, and hybrid ranking | |
| 0005.spec-mcp-and-cli.md | spec | MCP and CLI | Official MCP stdio tools and maintenance commands | |
| 0006.adr-mcp-sqlite-hybrid-architecture.md | adr | Architecture | Accepted SDK, SQLite, sqlite-vec, FTS5, and RRF decisions | |
