using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Tests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class MarkdownDocumentLoaderTests
{
    [Fact]
    public async Task LoadAsync_DefaultIncludeAndExclude_LoadsOnlyExpectedMarkdownFiles()
    {
        using var temp = new TemporaryDirectory();
        await CreateFilesAsync(temp.Path,
        [
            "root.md",
            "docs/a.md",
            "docs/b.txt",
            "docs/nested/c.md",
            "bin/generated.md",
            "obj/generated.md",
            ".git/ignored.md",
            "node_modules/pkg/readme.md",
            ".local-vector-search-mcp/index.md"
        ]);

        var documents = await LoadAsync(temp.Path);

        Assert.Equal(["docs/a.md", "docs/nested/c.md", "root.md"], Paths(documents));
    }

    [Fact]
    public async Task LoadAsync_CustomInclude_NarrowsScan()
    {
        using var temp = new TemporaryDirectory();
        await CreateFilesAsync(temp.Path, ["README.md", "docs/a.md", "notes/a.md", "src/readme.md"]);
        var config = new KnowledgeBaseConfig
        {
            Root = temp.Path,
            Include = ["docs/**/*.md", "README.md"],
            Exclude = []
        };

        var documents = await new MarkdownDocumentLoader().LoadAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(["docs/a.md", "README.md"], Paths(documents));
    }

    [Fact]
    public async Task LoadAsync_CustomExclude_RemovesIncludedFiles()
    {
        using var temp = new TemporaryDirectory();
        await CreateFilesAsync(temp.Path, ["docs/public/a.md", "docs/private/secret.md", "docs/private/nested/secret2.md"]);
        var config = new KnowledgeBaseConfig
        {
            Root = temp.Path,
            Include = ["docs/**/*.md"],
            Exclude = ["docs/private/**"]
        };

        var documents = await new MarkdownDocumentLoader().LoadAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(["docs/public/a.md"], Paths(documents));
    }

    [Fact]
    public async Task LoadAsync_ExcludePatterns_DoNotActAsSubstringFilters()
    {
        using var temp = new TemporaryDirectory();
        await CreateFilesAsync(temp.Path, ["binary-notes.md", "object-model.md", "src/bin/generated.md", "src/obj/generated.md"]);
        var config = new KnowledgeBaseConfig
        {
            Root = temp.Path,
            Include = ["**/*.md"],
            Exclude = ["**/bin/**", "**/obj/**"]
        };

        var documents = await new MarkdownDocumentLoader().LoadAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(["binary-notes.md", "object-model.md"], Paths(documents));
    }

    [Fact]
    public async Task LoadAsync_ReturnsNormalizedPathsInStableOrdinalIgnoreCaseOrder()
    {
        using var temp = new TemporaryDirectory();
        await CreateFilesAsync(temp.Path, ["z/last.md", "A/second.md", "a/Third.md", "middle.md"]);

        var first = await LoadAsync(temp.Path);
        var second = await LoadAsync(temp.Path);
        var paths = Paths(first);

        Assert.Equal(paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), paths);
        Assert.Equal(paths, Paths(second));
        Assert.All(paths, path => Assert.DoesNotContain('\\', path));
    }

    private static async Task<IReadOnlyList<Core.Markdown.MarkdownSourceDocument>> LoadAsync(string root)
        => await new MarkdownDocumentLoader().LoadAsync(
            new KnowledgeBaseConfig { Root = root },
            TestContext.Current.CancellationToken);

    private static string[] Paths(IReadOnlyList<Core.Markdown.MarkdownSourceDocument> documents)
        => documents.Select(document => document.RelativePath).ToArray();

    private static async Task CreateFilesAsync(string root, IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"# {relativePath}\n", TestContext.Current.CancellationToken);
        }
    }
}
