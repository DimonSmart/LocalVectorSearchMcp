namespace DimonSmart.LocalVectorSearchMcp.Core.Markdown;

public sealed record MarkdownSourceDocument(
    string RelativePath,
    string AbsolutePath,
    string Markdown,
    string ContentHash,
    DateTimeOffset LastWriteTimeUtc,
    bool HasUtf8Bom = false,
    string? SourceRevisionHash = null)
{
    public string SourceHash => SourceRevisionHash ?? ContentHash;
}
