using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using iText.IO.Font;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using PageSize = iText.Kernel.Geom.PageSize;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using YamlDotNet.Serialization;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using Text = iText.Layout.Element.Text;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class PdfBookService(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard) : IPdfBookService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .Build();

    private static readonly IDeserializer FrontMatterDeserializer = new DeserializerBuilder().Build();

    public Task<CreatePdfBookResponse> CreateAsync(
        CreatePdfBookRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritesEnabled();
        var sourcePath = pathGuard.ValidateRelativePath(request.Path);
        var sourceAbsolute = pathGuard.ResolveMarkdownPath(sourcePath);
        if (!File.Exists(sourceAbsolute))
            throw new DocumentNotFoundException(sourcePath);

        var outputPath = ResolveOutputPath(sourcePath, request.OutputPath);
        var outputAbsolute = pathGuard.ResolveWorkspacePath(outputPath);
        var parent = Path.GetDirectoryName(outputAbsolute)!;
        Directory.CreateDirectory(parent);
        outputAbsolute = pathGuard.ResolveWorkspacePath(outputPath);

        var temporaryPath = Path.Combine(
            parent,
            $".{Path.GetFileName(outputAbsolute)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var warnings = Render(sourceAbsolute, temporaryPath, request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputAbsolute, true);
            return Task.FromResult(new CreatePdfBookResponse(
                outputPath,
                new FileInfo(outputAbsolute).Length,
                warnings));
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private IReadOnlyList<string> Render(
        string sourcePath,
        string outputPath,
        CreatePdfBookRequest request,
        CancellationToken cancellationToken)
    {
        var presentation = BookPresentation.Get(request.PageFormat, request.Style);
        var warnings = new List<string>();
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(writer);
        using var document = new Document(pdf, presentation.PageSize);
        document.SetMargins(presentation.Margin, presentation.Margin, presentation.Margin, presentation.Margin);
        var fonts = PdfFonts.Load(presentation.FontFamily);
        var resources = new LocalImageResolver(sourcePath, pathGuard);
        var source = File.ReadAllText(sourcePath);
        var markdown = Markdig.Markdown.Parse(source, MarkdownPipeline);
        SetDocumentMetadata(pdf, markdown);
        foreach (var block in markdown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderBlock(block, element => document.Add(element), presentation, fonts, resources, warnings, 0);
        }
        return warnings;
    }

    private string ResolveOutputPath(string sourcePath, string? requestedPath)
    {
        var outputPath = requestedPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = Path.ChangeExtension(sourcePath, ".pdf")!.Replace('\\', '/');
        var normalized = pathGuard.ValidateRelativePath(outputPath);
        if (!Path.GetExtension(normalized).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new KnowledgeBaseAccessException("PDF export requires a .pdf output path.");
        return normalized;
    }

    private void EnsureWritesEnabled()
    {
        if (!config.KnowledgeBase.AllowWrites)
            throw new WorkspaceMutationException(
                "Workspace writes are disabled. Set knowledgeBase.allowWrites to true to enable PDF export.");
    }

    private static void RenderBlock(Block block, Action<IBlockElement> add, BookPresentation presentation, PdfFonts fonts, LocalImageResolver resources, List<string> warnings, int quoteDepth)
    {
        switch (block)
        {
            case YamlFrontMatterBlock:
                break;
            case HeadingBlock heading:
                var level = Math.Clamp(heading.Level, 1, 6);
                add(CreateParagraph(heading.Inline, presentation, fonts, presentation.HeadingSizes[level - 1], true, resources, warnings)
                    .SetMarginTop(level == 1 ? presentation.ParagraphSpacing * 3 : presentation.ParagraphSpacing * 2)
                    .SetKeepWithNext(true));
                break;
            case ParagraphBlock paragraph:
                add(CreateParagraph(paragraph.Inline, presentation, fonts, presentation.BodySize, false, resources, warnings));
                break;
            case FencedCodeBlock fencedCode:
                add(CreateCodeParagraph(fencedCode, presentation, fonts).SetBackgroundColor(new DeviceRgb(245, 245, 245)).SetPadding(5));
                break;
            case CodeBlock code:
                add(CreateCodeParagraph(code, presentation, fonts).SetBackgroundColor(new DeviceRgb(245, 245, 245)).SetPadding(5));
                break;
            case ListBlock list:
                add(CreateList(list, presentation, fonts, resources, warnings, quoteDepth));
                break;
            case QuoteBlock quote:
                var quoteContainer = new Div()
                    .SetMarginLeft(quoteDepth < 4 ? presentation.QuoteIndent : 0)
                    .SetBorderLeft(new SolidBorder(new DeviceRgb(120, 120, 120), 1))
                    .SetPaddingLeft(8);
                foreach (var child in quote)
                    RenderBlock(child, element => quoteContainer.Add(element), presentation, fonts, resources, warnings, quoteDepth + 1);
                add(quoteContainer);
                break;
            case ThematicBreakBlock:
                add(new Div().SetBorderBottom(new SolidBorder(.75f))
                    .SetMarginTop(presentation.ParagraphSpacing)
                    .SetMarginBottom(presentation.ParagraphSpacing));
                break;
            case MarkdownTable table:
                var renderedTable = CreateTable(table, presentation, fonts, resources, warnings);
                if (renderedTable is not null) add(renderedTable);
                break;
            default:
                warnings.Add($"Unsupported Markdown block '{block.GetType().Name}' was not rendered.");
                break;
        }
    }

    private static Paragraph CreateParagraph(ContainerInline? inline, BookPresentation presentation, PdfFonts fonts, float size, bool bold, LocalImageResolver resources, List<string> warnings)
    {
        var paragraph = new Paragraph().SetFont(bold ? fonts.Bold : fonts.Regular)
            .SetFontSize(size).SetMultipliedLeading(presentation.LeadingMultiplier);
        AddInlines(paragraph, inline, presentation, fonts, new InlineStyle(bold, false, false, false), size, resources, warnings);
        return paragraph.SetMarginBottom(presentation.ParagraphSpacing);
    }

    private static void AddInlines(Paragraph paragraph, ContainerInline? inline, BookPresentation presentation, PdfFonts fonts, InlineStyle style, float size, LocalImageResolver resources, List<string> warnings)
    {
        if (inline is null) return;
        for (var node = inline.FirstChild; node is not null; node = node.NextSibling)
        {
            switch (node)
            {
                case LiteralInline literal: AddText(paragraph, literal.Content.ToString(), presentation, fonts, style, size); break;
                case CodeInline code: AddText(paragraph, code.Content, presentation, fonts, style with { Code = true }, size); break;
                case LineBreakInline: paragraph.Add("\n"); break;
                case EmphasisInline emphasis:
                    AddInlines(paragraph, emphasis, presentation, fonts, style with
                    {
                        Bold = style.Bold || emphasis.DelimiterCount >= 2,
                        Italic = style.Italic || (emphasis.DelimiterCount == 1 && emphasis.DelimiterChar != '~'),
                        Strike = style.Strike || emphasis.DelimiterChar == '~'
                    }, size, resources, warnings);
                    break;
                case TaskList task: AddText(paragraph, task.Checked ? "☑ " : "☐ ", presentation, fonts, style, size); break;
                case AutolinkInline autolink: AddLink(paragraph, autolink.Url, autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url, presentation, fonts, style, size); break;
                case LinkInline image when image.IsImage: AddImage(paragraph, image, presentation, resources, warnings); break;
                case LinkInline link: AddLink(paragraph, InlineText(link), link.Url, presentation, fonts, style, size); break;
                case ContainerInline container: AddInlines(paragraph, container, presentation, fonts, style, size, resources, warnings); break;
            }
        }
    }

    private static void AddLink(Paragraph paragraph, string label, string? target, BookPresentation presentation, PdfFonts fonts, InlineStyle style, float size)
    {
        if (string.IsNullOrWhiteSpace(target)) { AddText(paragraph, label, presentation, fonts, style, size); return; }
        if (string.IsNullOrEmpty(label)) label = target;
        var link = new Link(label, PdfAction.CreateURI(target)).SetFont(FontFor(fonts, style))
            .SetFontSize(style.Code ? presentation.InlineCodeSize : size).SetFontColor(ColorConstants.BLUE);
        if (style.Strike) link.SetLineThrough();
        paragraph.Add(link);
    }

    private static void AddImage(Paragraph paragraph, LinkInline image, BookPresentation presentation, LocalImageResolver resources, List<string> warnings)
    {
        var source = image.Url ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(InlineText(image)) ? source : InlineText(image);
        try
        {
            var rendered = new Image(ImageDataFactory.Create(resources.ReadImage(source)));
            rendered.ScaleToFit(presentation.PageSize.GetWidth() - 2 * presentation.Margin, presentation.PageSize.GetHeight() - 2 * presentation.Margin);
            paragraph.Add(rendered);
        }
        catch (Exception exception)
        {
            warnings.Add($"Image '{source}' could not be loaded: {exception.Message}");
            paragraph.Add(new Text($"[Image unavailable: {description}]").SetFontColor(ColorConstants.RED));
        }
    }

    private static void AddText(Paragraph paragraph, string value, BookPresentation presentation, PdfFonts fonts, InlineStyle style, float size)
    {
        var text = new Text(value).SetFont(FontFor(fonts, style)).SetFontSize(style.Code ? presentation.InlineCodeSize : size);
        if (style.Strike) text.SetLineThrough();
        paragraph.Add(text);
    }

    private static PdfFont FontFor(PdfFonts fonts, InlineStyle style) => style.Code ? fonts.Monospace : style.Bold && style.Italic ? fonts.BoldItalic : style.Bold ? fonts.Bold : style.Italic ? fonts.Italic : fonts.Regular;
    private static string InlineText(ContainerInline inline) => string.Concat(inline.Descendants().OfType<LiteralInline>().Select(literal => literal.Content.ToString()));
    private static Paragraph CreateCodeParagraph(CodeBlock source, BookPresentation presentation, PdfFonts fonts) => new Paragraph(string.Join("\n", source.Lines.Lines.Select(line => line.Slice.ToString()))).SetFont(fonts.Monospace).SetFontSize(presentation.CodeSize).SetMultipliedLeading(1).SetMarginBottom(presentation.ParagraphSpacing);

    private static iText.Layout.Element.List CreateList(ListBlock source, BookPresentation presentation, PdfFonts fonts, LocalImageResolver resources, List<string> warnings, int quoteDepth)
    {
        var list = source.IsOrdered ? new iText.Layout.Element.List(ListNumberingType.DECIMAL) : new iText.Layout.Element.List();
        if (source.IsOrdered && int.TryParse(source.OrderedStart, out var start)) list.SetItemStartIndex(start);
        list.SetSymbolIndent(presentation.ListIndent).SetMarginBottom(presentation.ParagraphSpacing);
        foreach (var item in source.OfType<ListItemBlock>())
        {
            var result = new ListItem();
            foreach (var block in item)
                RenderBlock(block, element => result.Add(element), presentation, fonts, resources, warnings, quoteDepth);
            list.Add(result);
        }
        return list;
    }

    private static iText.Layout.Element.Table? CreateTable(MarkdownTable source, BookPresentation presentation, PdfFonts fonts, LocalImageResolver resources, List<string> warnings)
    {
        var count = source.OfType<TableRow>().Select(row => row.OfType<TableCell>().Sum(cell => Math.Max(1, cell.ColumnSpan))).DefaultIfEmpty().Max();
        if (count == 0) return null;
        var table = new iText.Layout.Element.Table(count).UseAllAvailableWidth().SetMarginBottom(presentation.ParagraphSpacing);
        foreach (var row in source.OfType<TableRow>())
        {
            var column = 0;
            foreach (var cellSource in row.OfType<TableCell>())
            {
                var span = Math.Max(1, cellSource.ColumnSpan);
                var cell = new Cell(Math.Max(1, cellSource.RowSpan), span).SetPadding(3);
                var alignment = source.ColumnDefinitions is not null && column < source.ColumnDefinitions.Count ? source.ColumnDefinitions[column].Alignment : (TableColumnAlign?)null;
                if (alignment == TableColumnAlign.Left) cell.SetTextAlignment(TextAlignment.LEFT);
                if (alignment == TableColumnAlign.Center) cell.SetTextAlignment(TextAlignment.CENTER);
                if (alignment == TableColumnAlign.Right) cell.SetTextAlignment(TextAlignment.RIGHT);
                foreach (var paragraph in cellSource.OfType<ParagraphBlock>()) cell.Add(CreateParagraph(paragraph.Inline, presentation, fonts, presentation.TableSize, row.IsHeader, resources, warnings));
                if (row.IsHeader) table.AddHeaderCell(cell); else table.AddCell(cell);
                column += span;
            }
        }
        return table;
    }

    private static void SetDocumentMetadata(PdfDocument pdf, MarkdownDocument markdown)
    {
        var frontMatter = markdown.OfType<YamlFrontMatterBlock>().FirstOrDefault();
        if (frontMatter is null) return;

        Dictionary<string, string>? metadata;
        try
        {
            metadata = FrontMatterDeserializer.Deserialize<Dictionary<string, string>>(string.Join("\n", frontMatter.Lines.Lines.Select(line => line.Slice.ToString())));
        }
        catch
        {
            return;
        }

        if (metadata is null) return;
        if (metadata.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title)) pdf.GetDocumentInfo().SetTitle(title);
        if (metadata.TryGetValue("author", out var author) && !string.IsNullOrWhiteSpace(author)) pdf.GetDocumentInfo().SetAuthor(author);
        if (metadata.TryGetValue("language", out var language) && !string.IsNullOrWhiteSpace(language)) pdf.GetCatalog().SetLang(new PdfString(language));
    }

    private sealed record InlineStyle(bool Bold, bool Italic, bool Strike, bool Code);

    private sealed record BookPresentation(PageSize PageSize, float Margin, bool Serif, float BodySize, float LeadingMultiplier, float ParagraphSpacing, float[] HeadingSizes, float CodeSize, float InlineCodeSize, float TableSize, float ListIndent, float QuoteIndent)
    {
        public BookFontFamily FontFamily => Serif ? BookFontFamily.Serif : BookFontFamily.SansSerif;
        public static BookPresentation Get(PdfBookPageFormat format, PdfBookStyle style)
        {
            var page = format switch { PdfBookPageFormat.A4 => PageSize.A4, PdfBookPageFormat.A5 => PageSize.A5, PdfBookPageFormat.A6 => PageSize.A6, PdfBookPageFormat.B5 => PageSize.B5, PdfBookPageFormat.Letter => PageSize.LETTER, _ => throw new ArgumentOutOfRangeException(nameof(format)) };
            return style switch
            {
                PdfBookStyle.Compact => new(page, 28.35f, false, 10, 1.05f, 3, [16, 13, 11, 10.5f, 10, 10], 8.5f, 9, 8.5f, 14, 14),
                PdfBookStyle.Classic => new(page, 62, true, 11, 1.2f, 6, [18, 14, 12, 11.5f, 11, 11], 9.5f, 10, 9.5f, 18, 18),
                PdfBookStyle.Modern => new(page, 56.7f, false, 11, 1.25f, 8, [20, 15, 12, 11.5f, 11, 11], 9.5f, 10, 9.5f, 18, 18),
                PdfBookStyle.Technical => new(page, 42.5f, false, 10, 1.15f, 5, [17, 13, 11, 10.5f, 10, 10], 9, 9.5f, 9, 16, 16),
                PdfBookStyle.Academic => new(page, 70.9f, true, 11, 1.3f, 6, [16, 13, 11, 11, 11, 11], 9.5f, 10, 9.5f, 18, 20),
                _ => throw new ArgumentOutOfRangeException(nameof(style))
            };
        }
    }

    private enum BookFontFamily { SansSerif, Serif }
    private sealed record PdfFonts(PdfFont Regular, PdfFont Bold, PdfFont Italic, PdfFont BoldItalic, PdfFont Monospace)
    {
        public static PdfFonts Load(BookFontFamily family)
        {
            var serif = family == BookFontFamily.Serif;
            return new(
                Create(serif ? "times.ttf" : "arial.ttf", serif ? "DejaVuSerif.ttf" : "DejaVuSans.ttf", serif ? "Times New Roman.ttf" : "Arial.ttf"),
                Create(serif ? "timesbd.ttf" : "arialbd.ttf", serif ? "DejaVuSerif-Bold.ttf" : "DejaVuSans-Bold.ttf", serif ? "Times New Roman Bold.ttf" : "Arial Bold.ttf"),
                Create(serif ? "timesi.ttf" : "ariali.ttf", serif ? "DejaVuSerif-Italic.ttf" : "DejaVuSans-Oblique.ttf", serif ? "Times New Roman Italic.ttf" : "Arial Italic.ttf"),
                Create(serif ? "timesbi.ttf" : "arialbi.ttf", serif ? "DejaVuSerif-BoldItalic.ttf" : "DejaVuSans-BoldOblique.ttf", serif ? "Times New Roman Bold Italic.ttf" : "Arial Bold Italic.ttf"),
                Create("consola.ttf", "DejaVuSansMono.ttf", "Courier New.ttf"));
        }
        private static PdfFont Create(params string[] names)
        {
            foreach (var name in names) foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "/usr/share/fonts/truetype/dejavu", "/usr/local/share/fonts", "/System/Library/Fonts/Supplemental", "/Library/Fonts" })
            {
                var candidate = Path.Combine(root, name);
                if (File.Exists(candidate)) return PdfFontFactory.CreateFont(candidate, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
            }
            throw new InvalidOperationException("No Unicode-capable system font is available.");
        }
    }

    private sealed class LocalImageResolver(string sourcePath, KnowledgeBasePathGuard pathGuard)
    {
        private readonly string _directory = Path.GetDirectoryName(sourcePath)!;
        public byte[] ReadImage(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || Uri.TryCreate(source, UriKind.Absolute, out _)) throw new InvalidOperationException("Only local relative image paths are supported.");
            var path = Path.GetFullPath(Path.Combine(_directory, source.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(Path.GetDirectoryName(pathGuard.ResolveWorkspacePath(".placeholder"))!);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Image path is outside configured knowledge base root.");
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            return File.ReadAllBytes(pathGuard.ResolveWorkspacePath(relative));
        }
    }
}
