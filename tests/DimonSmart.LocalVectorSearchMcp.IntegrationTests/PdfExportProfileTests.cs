using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using Matrix = iText.Kernel.Geom.Matrix;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SkiaSharp;
using ModelContextProtocol.Client;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class PdfExportProfileTests
{
    [Theory]
    [InlineData(PdfExportProfile.Screen, "\"screen\"")]
    [InlineData(PdfExportProfile.Standard, "\"standard\"")]
    [InlineData(PdfExportProfile.Print, "\"print\"")]
    public void Profile_HasCanonicalJsonRepresentation(PdfExportProfile profile, string json)
    {
        Assert.Equal(json, JsonSerializer.Serialize(profile));
        Assert.Equal(profile, JsonSerializer.Deserialize<PdfExportProfile>(json));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("\"invalid\"")]
    [InlineData("null")]
    public void Profile_RejectsInvalidJson(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PdfExportProfile>(json));

    [Fact]
    public async Task CreateAsync_RejectsInvalidEnumBeforeReplacingExistingPdf()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"), "# Test");
        var pdfPath = Path.Combine(directory.Path, "book.pdf");
        await File.WriteAllBytesAsync(pdfPath, [1, 2, 3, 4]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            MakeService(directory).CreateAsync(new CreatePdfBookRequest("book.md",
                Profile: (PdfExportProfile)123), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(pdfPath));
        Assert.Single(Directory.GetFiles(directory.Path, "*.pdf"));
    }

    [Fact]
    public async Task HighResolutionJpeg_IsDownsampledButPhysicalGeometryIsUnchanged()
    {
        using var directory = new TemporaryDirectory();
        const int sourceWidth = 2400;
        const int sourceHeight = 1800;
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "photo.jpg"),
            MakeJpeg(sourceWidth, sourceHeight));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"),
            "# Photo\n\n![first](photo.jpg)\n\n![again](photo.jpg)\n");
        var service = MakeService(directory);

        var screen = await service.CreateAsync(new CreatePdfBookRequest("book.md",
            "screen.pdf", Profile: PdfExportProfile.Screen), TestContext.Current.CancellationToken);
        var standard = await service.CreateAsync(new CreatePdfBookRequest("book.md",
            "standard.pdf"), TestContext.Current.CancellationToken);
        var print = await service.CreateAsync(new CreatePdfBookRequest("book.md",
            "print.pdf", Profile: PdfExportProfile.Print), TestContext.Current.CancellationToken);

        Assert.Empty(screen.Warnings);
        Assert.Empty(standard.Warnings);
        Assert.Empty(print.Warnings);
        Assert.True(screen.Bytes < print.Bytes);
        Assert.True(screen.Bytes < standard.Bytes);

        var screenImages = ReadImages(Path.Combine(directory.Path, screen.Path));
        var standardImages = ReadImages(Path.Combine(directory.Path, standard.Path));
        var printImages = ReadImages(Path.Combine(directory.Path, print.Path));
        Assert.Equal(2, screenImages.Count);
        Assert.Equal(2, standardImages.Count);
        Assert.Equal(2, printImages.Count);

        // The same image uses one resource, although each occurrence has its own layout.
        Assert.Single(screenImages.Select(i => i.Reference).Distinct());
        Assert.Single(standardImages.Select(i => i.Reference).Distinct());
        Assert.Single(printImages.Select(i => i.Reference).Distinct());
        Assert.All(screenImages, image => Assert.True(image.PixelsWide < sourceWidth));
        Assert.True(screenImages[0].PixelsWide < standardImages[0].PixelsWide);
        Assert.True(standardImages[0].PixelsWide <= printImages[0].PixelsWide);

        for (var i = 0; i < screenImages.Count; i++)
        {
            Assert.InRange(Math.Abs(screenImages[i].WidthPt - printImages[i].WidthPt), 0, 0.2);
            Assert.InRange(Math.Abs(screenImages[i].HeightPt - printImages[i].HeightPt), 0, 0.2);
            Assert.InRange(Math.Abs(screenImages[i].WidthPt - standardImages[i].WidthPt), 0, 0.2);
            Assert.InRange(Math.Abs(screenImages[i].HeightPt - standardImages[i].HeightPt), 0, 0.2);
        }
    }

    [Fact]
    public async Task PackagedExecutable_PdfExportSmoke()
    {
        var executable = Environment.GetEnvironmentVariable("PDF_EXPORT_SMOKE_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable)) return;

        using var directory = new TemporaryDirectory();
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "photo.jpg"),
            MakeJpeg(1600, 1200), token);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "book.md"),
            "# Packaged PDF smoke\n\n![photo](photo.jpg)\n", token);
        var configPath = Path.Combine(directory.Path, "config.yml");
        var root = directory.Path.Replace('\\\\', '/');
        var yaml = $"knowledgeBase:\n  root: \"{root}\"\n  allowWrites: true\n  watchFiles: false\nstorage:\n  path: \"{root}/index.db\"\n";
        await File.WriteAllTextAsync(configPath, yaml, token);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "packaged-pdf-export",
            Command = executable,
            Arguments = ["--config", configPath],
            WorkingDirectory = directory.Path
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: token);
        var response = await client.CallToolAsync("kb_create_pdf_book",
            new Dictionary<string, object?>
            {
                ["request"] = new { path = "book.md", profile = "screen" }
            }, cancellationToken: token);
        Assert.False(response.IsError is true);
        var images = ReadImages(Path.Combine(directory.Path, "book.pdf"));
        Assert.Single(images);
        Assert.True(images[0].PixelsWide < 1600);
    }

    private static PdfBookService MakeService(TemporaryDirectory directory)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = directory.Path, AllowWrites = true },
            Storage = new StorageConfig { Path = Path.Combine(directory.Path, ".index.db") }
        };
        return new PdfBookService(config, new KnowledgeBasePathGuard(config));
    }

    private static byte[] MakeJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        var random = new Random(42);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256),
                    (byte)random.Next(256), (byte)random.Next(256)));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    private static List<(string Reference, int PixelsWide, float WidthPt, float HeightPt)> ReadImages(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        var result = new List<(string, int, float, float)>();
        for (var pageNo = 1; pageNo <= pdf.GetNumberOfPages(); pageNo++)
        {
            var listener = new ImageListener(result);
            new PdfCanvasProcessor(listener).ProcessPageContent(pdf.GetPage(pageNo));
        }
        return result;
    }

    private sealed class ImageListener(List<(string, int, float, float)> images) : IEventListener
    {
        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_IMAGE) return;
            var info = (ImageRenderInfo)data;
            var xObject = info.GetImage();
            var dictionary = xObject.GetPdfObject();
            if (dictionary.GetAsName(PdfName.Subtype) != PdfName.Image) return;
            var width = dictionary.GetAsNumber(PdfName.Width).IntValue();
            var ctm = info.GetImageCtm();
            images.Add((dictionary.GetIndirectReference().ToString(), width,
                (float)Math.Sqrt(Math.Pow(ctm.Get(Matrix.I11), 2) + Math.Pow(ctm.Get(Matrix.I21), 2)),
                (float)Math.Sqrt(Math.Pow(ctm.Get(Matrix.I12), 2) + Math.Pow(ctm.Get(Matrix.I22), 2))));
        }

        public ICollection<EventType>? GetSupportedEvents() => [EventType.RENDER_IMAGE];
    }
}
