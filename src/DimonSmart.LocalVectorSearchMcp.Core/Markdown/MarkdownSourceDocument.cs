namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed record MarkdownSourceDocument(
    string KnowledgeBase,
    string RelativePath,
    string AbsolutePath,
    string Markdown,
    string ContentHash,
    DateTimeOffset LastWriteTimeUtc);
