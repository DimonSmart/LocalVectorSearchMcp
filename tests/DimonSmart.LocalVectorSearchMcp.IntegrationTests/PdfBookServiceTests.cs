using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

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

    [Fact]
    public async Task CreateAsync_PreservesNestedQuoteContentInPdfText()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"),
            "> Совет мастера:\n>\n> - Первый пункт\n> - Второй пункт\n>   - Вложенный пункт\n>\n> | Блюдо | Оценка |\n> |---|---:|\n> | Шашлык | 5 |\n>\n> > Вложенная цитата.\n>\n> ```csharp\n> Console.WriteLine(\"Готово\");\n> ```\n>\n> Продолжение цитаты.\n",
            TestContext.Current.CancellationToken);

        var result = await CreateService(directory).CreateAsync(new CreatePdfBookRequest("book.md"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Warnings);
        var text = ExtractText(Path.Combine(directory.Path, result.Path));
        Assert.Contains("Совет мастера", text, StringComparison.Ordinal);
        Assert.Contains("Первый пункт", text, StringComparison.Ordinal);
        Assert.Contains("Второй пункт", text, StringComparison.Ordinal);
        Assert.Contains("Вложенный пункт", text, StringComparison.Ordinal);
        Assert.Contains("Шашлык", text, StringComparison.Ordinal);
        Assert.Contains("Вложенная цитата", text, StringComparison.Ordinal);
        Assert.Contains("Console.WriteLine", text, StringComparison.Ordinal);
        Assert.Contains("Продолжение цитаты", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ExcludesFrontMatterAndSetsPdfMetadata()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"),
            "---\ntitle: Книга рецептов\nauthor: QA Test\nlanguage: ru\n---\n\n# Шашлык\n\nОсновной текст.\n",
            TestContext.Current.CancellationToken);

        var result = await CreateService(directory).CreateAsync(new CreatePdfBookRequest("book.md"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Warnings);
        var pdfPath = Path.Combine(directory.Path, result.Path);
        var text = ExtractText(pdfPath);
        Assert.Contains("Шашлык", text, StringComparison.Ordinal);
        Assert.Contains("Основной текст", text, StringComparison.Ordinal);
        Assert.DoesNotContain("title: Книга рецептов", text, StringComparison.Ordinal);
        Assert.DoesNotContain("author: QA Test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("language: ru", text, StringComparison.Ordinal);

        using var pdf = new PdfDocument(new PdfReader(pdfPath));
        Assert.Equal("Книга рецептов", pdf.GetDocumentInfo().GetTitle());
        Assert.Equal("QA Test", pdf.GetDocumentInfo().GetAuthor());
        Assert.Equal("ru", pdf.GetCatalog().GetLang().ToUnicodeString());
    }

    private static PdfBookService CreateService(TemporaryDirectory directory)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = directory.Path, AllowWrites = true },
            Storage = new StorageConfig { Path = Path.Combine(directory.Path, ".index.db") }
        };
        return new PdfBookService(config, new KnowledgeBasePathGuard(config));
    }

    private static string ExtractText(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        return string.Join("\n", Enumerable.Range(1, pdf.GetNumberOfPages())
            .Select(page => PdfTextExtractor.GetTextFromPage(pdf.GetPage(page))));
    }

    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL6aQAAAABJRU5ErkJggg==");
}
