namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public sealed record MarkdownSourceDocument(
    string RelativePath,
    string AbsolutePath,
    string Markdown,
    string ContentHash,
    DateTimeOffset LastWriteTimeUtc);
