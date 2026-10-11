using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class PdfBookServiceTests
{
    [Fact]
    public async Task CreateAsync_RendersFormattedMarkdownTableAndLocalImage()
    {
        using var directory = new TemporaryDirectory();
        var imagePath = Path.Combine(directory.Path, "pixel.png");
        await File.WriteAllBytesAsync(imagePath, Pixel, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"),
            "# Заголовок\n\n**Bold** and `code`.\n\n![pixel](pixel.png)\n\n| A | B |\n|---|--:|\n| left | 2 |\n",
            TestContext.Current.CancellationToken);
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = directory.Path, AllowWrites = true },
            Storage = new StorageConfig { Path = Path.Combine(directory.Path, ".index.db") }
        };
        var service = new PdfBookService(config, new KnowledgeBasePathGuard(config));

        var result = await service.CreateAsync(new CreatePdfBookRequest("book.md"), TestContext.Current.CancellationToken);

        Assert.Equal("book.pdf", result.Path);
        Assert.Empty(result.Warnings);
        Assert.True(result.Bytes > 4);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory.Path, result.Path), TestContext.Current.CancellationToken);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"), "# Replaced", TestContext.Current.CancellationToken);
        var replaced = await service.CreateAsync(new CreatePdfBookRequest("book.md"), TestContext.Current.CancellationToken);
        Assert.Equal(result.Path, replaced.Path);
        Assert.True(replaced.Bytes > 4);
    }

    [Fact]
    public async Task CreateAsync_LeavesWarningAndPlaceholderForUnavailableImage()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"), "![missing](missing.png)", TestContext.Current.CancellationToken);
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = directory.Path, AllowWrites = true },
            Storage = new StorageConfig { Path = Path.Combine(directory.Path, ".index.db") }
        };

        var result = await new PdfBookService(config, new KnowledgeBasePathGuard(config)).CreateAsync(new CreatePdfBookRequest("book.md"), TestContext.Current.CancellationToken);

        Assert.Single(result.Warnings);
        Assert.Contains("missing.png", result.Warnings[0], StringComparison.Ordinal);
    }

    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL6aQAAAABJRU5ErkJggg==");
}
