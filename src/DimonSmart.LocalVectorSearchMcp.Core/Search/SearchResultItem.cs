using System.ComponentModel;

namespace DimonSmart.LocalVectorSearchMcp.Core.Search;

public sealed record SearchResultItem(
    string Path,
    [property: Description("Pointer to the first element of the indexed search chunk; not necessarily the exact match.")]
    string Pointer,
    [property: Description("Pointer qualified with its source file identifier.")]
    string FullPointer,
    [property: Description("Relevance score; interpretation differs between semantic, lexical and hybrid search.")]
    double Score,
    SearchMode SearchMode,
    string? HeadingPath,
    string Snippet,
    [property: Description("Suggested arguments for a subsequent kb_read call.")]
    ReadHint ReadHint,
    [property: Description("Hash of the indexed source revision, which may lag behind the current file.")]
    string IndexedSourceHash);
