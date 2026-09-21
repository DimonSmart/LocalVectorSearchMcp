using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class WorkspaceImagePolicyTests
{
    [Fact]
    public void FormatDetectionRecognizesSupportedSignatures()
    {
        Assert.Equal(
            WorkspaceImageFormat.Png,
            WorkspaceImageFormats.Detect(
                [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])?.Format);
        Assert.Equal(
            WorkspaceImageFormat.Jpeg,
            WorkspaceImageFormats.Detect(
                [0xff, 0xd8, 0xff, 0x00])?.Format);
        Assert.Equal(
            WorkspaceImageFormat.WebP,
            WorkspaceImageFormats.Detect(
                "RIFF1234WEBP"u8)?.Format);
        Assert.Equal(
            WorkspaceImageFormat.Gif,
            WorkspaceImageFormats.Detect(
                "GIF87a"u8)?.Format);
        Assert.Equal(
            WorkspaceImageFormat.Gif,
            WorkspaceImageFormats.Detect(
                "GIF89a"u8)?.Format);
        Assert.Null(
            WorkspaceImageFormats.Detect(
                "not an image"u8));
    }

    [Fact]
    public void MimeValidationUsesDetectedFormatAsAuthority()
    {
        var png = WorkspaceImageFormats.Detect(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])!;

        WorkspaceImageFormats.ValidateDeclaredMime(null, png);
        WorkspaceImageFormats.ValidateDeclaredMime("", png);
        WorkspaceImageFormats.ValidateDeclaredMime(
            "application/octet-stream",
            png);
        WorkspaceImageFormats.ValidateDeclaredMime(
            "image/png",
            png);

        Assert.Throws<WorkspaceImageException>(
            () => WorkspaceImageFormats.ValidateDeclaredMime(
                "image/jpeg",
                png));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../x.png")]
    [InlineData("a/b.png")]
    [InlineData("a\\b.png")]
    [InlineData("C:\\x.png")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("CON.png")]
    [InlineData("nul")]
    [InlineData("COM1.jpg")]
    [InlineData("LPT9.webp")]
    [InlineData("x.txt")]
    public void ExplicitFileNameRejectsUnsafeOrMismatchedNames(
        string fileName)
    {
        var png = WorkspaceImageFormats.Detect(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])!;

        Assert.Throws<WorkspaceImageException>(
            () => ImageFileNamePolicy.Resolve(
                fileName,
                null,
                png));
    }

    [Fact]
    public void ExplicitFileNameSupportsUnicodeAndAddsMissingExtension()
    {
        var png = WorkspaceImageFormats.Detect(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])!;

        Assert.Equal(
            "обложка.png",
            ImageFileNamePolicy.Resolve(
                "обложка",
                null,
                png));
        Assert.Equal(
            "cover.PNG",
            ImageFileNamePolicy.Resolve(
                "cover.PNG",
                null,
                png));
    }

    [Fact]
    public void SourceFileNameIsOnlyANameHint()
    {
        var png = WorkspaceImageFormats.Detect(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])!;

        Assert.Equal(
            "cover.png",
            ImageFileNamePolicy.Resolve(
                null,
                "../folder/cover.jpg",
                png));

        var generated = ImageFileNamePolicy.Resolve(
            null,
            "CON.jpg",
            png);
        Assert.EndsWith(".png", generated, StringComparison.Ordinal);
        Assert.NotEqual("CON.png", generated);
    }

    [Fact]
    public void MarkdownReferenceEscapesAltTextAndComplexDestination()
    {
        Assert.Equal(
            @"![A \[b\]\\ C](<images/my image.png>)",
            ImageFileNamePolicy.BuildMarkdown(
                "images/my image.png",
                "A [b]\\\nC"));
        Assert.Equal(
            "![](images/cover.png)",
            ImageFileNamePolicy.BuildMarkdown(
                "images/cover.png",
                null));
    }

    [Fact]
    public void ImageCursorRoundTripsAndRejectsInvalidVersions()
    {
        const string path = "images/chapter/Диаграмма.png";
        var cursor = ImageListCursor.Encode(path);

        Assert.Equal(path, ImageListCursor.Decode(cursor));
        Assert.Throws<WorkspaceImageException>(
            () => ImageListCursor.Decode("not-base64!"));

        var unsupported = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("v2\nimages/a.png"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        Assert.Throws<WorkspaceImageException>(
            () => ImageListCursor.Decode(unsupported));
    }
}
