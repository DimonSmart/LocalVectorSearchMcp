using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Links;

internal sealed record ParsedLink(
    string TargetPath, string Destination, string? Fragment, string Text,
    string Kind, int Line, int Column, int Offset, int Length);

internal sealed record ParsedGraphDocument(
    IReadOnlyList<ParsedLink> Links, IReadOnlyList<string> Anchors);

/// <summary>Uses the same Markdig pipeline as source-aware Markdown parsing.</summary>
internal sealed class MarkdownLinkExtractor(KnowledgeBasePathGuard guard)
{
    public ParsedGraphDocument Parse(MarkdownSourceDocument document)
    {
        var syntax = Markdig.Markdown.Parse(document.Markdown,
            DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown.MarkdownPipelines.Tables);
        var anchors = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var heading in syntax.Descendants().OfType<HeadingBlock>())
        {
            var slug = Slug(VisibleText(heading.Inline));
            var ordinal = seen.GetValueOrDefault(slug);
            seen[slug] = ordinal + 1;
            anchors.Add(ordinal == 0 ? slug : slug + "-" + ordinal);
        }

        var links = new List<ParsedLink>();
        foreach (var link in syntax.Descendants().OfType<LinkInline>())
        {
            if (link.IsImage || link.IsAutoLink || link.Span.Start < 0)
                continue;
            var destination = link.Url ?? link.GetDynamicUrl?.Invoke();
            if (destination is null
                || !TryResolve(document.RelativePath, destination, out var target, out var fragment))
                continue;
            var kind = link.Reference is null ? "inline"
                : link.IsShortcut ? "shortcut"
                : document.Markdown.AsSpan(link.Span.Start,
                    Math.Min(document.Markdown.Length - link.Span.Start,
                        link.Span.End - link.Span.Start + 1)).EndsWith("[]".AsSpan())
                    ? "collapsed" : "reference";
            links.Add(new ParsedLink(target!, destination, fragment,
                VisibleText(link), kind, link.Line + 1, link.Column + 1,
                link.Span.Start, link.Span.End - link.Span.Start + 1));
        }
        return new ParsedGraphDocument(links, anchors);
    }

    public bool TryResolve(string source, string destination,
        out string? target, out string? fragment)
    {
        target = null;
        fragment = null;
        if (destination.StartsWith('/') || destination.StartsWith('\\')
            || destination.StartsWith("//", StringComparison.Ordinal)
            || Uri.TryCreate(destination, UriKind.Absolute, out _))
            return false;

        var hash = destination.IndexOf('#');
        var withoutFragment = hash < 0 ? destination : destination[..hash];
        var query = withoutFragment.IndexOf('?');
        var rawPath = query < 0 ? withoutFragment : withoutFragment[..query];
        var rawFragment = hash < 0 ? null : destination[(hash + 1)..];

        if (!TryDecode(rawPath, out var path) || path!.Contains('\\')
            || !TryDecode(rawFragment ?? "", out var decodedFragment))
            return false;
        if (path.Length == 0 && hash < 0) return false;
        if (path.Length > 0 && !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = new List<string>();
        if (path.Length > 0)
        {
            parts.AddRange(source.Split('/')[..^1]);
            foreach (var component in path.Split('/'))
            {
                if (component == ".") continue;
                if (component == "..")
                {
                    if (parts.Count == 0) return false;
                    parts.RemoveAt(parts.Count - 1);
                }
                else
                {
                    if (component.Length == 0) return false;
                    parts.Add(component);
                }
            }
        }
        var relative = path.Length == 0 ? source : string.Join("/", parts);
        try
        {
            relative = guard.ValidateRelativePath(relative);
            _ = guard.ResolveMarkdownPath(relative);
        }
        catch (DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases.KnowledgeBaseAccessException)
        {
            return false;
        }
        target = relative;
        fragment = rawFragment is null ? null : decodedFragment;
        return true;
    }

    private static bool TryDecode(string value, out string? decoded)
    {
        decoded = null;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') continue;
            if (i + 2 >= value.Length
                || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2]))
                return false;
            var code = Convert.ToInt32(value.Substring(i + 1, 2), 16);
            if (code is 0x2f or 0x5c or 0x00) return false;
            i += 2;
        }
        try
        {
            decoded = Uri.UnescapeDataString(value);
            return !decoded.Any(char.IsControl) && !decoded.Contains('%');
        }
        catch (UriFormatException) { return false; }
    }

    internal static string Slug(string title)
    {
        var buffer = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c)) buffer.Append('-');
            else if (char.IsLetterOrDigit(c) || char.IsSurrogate(c) || c is '-' or '_')
                buffer.Append(c);
        }
        return buffer.ToString();
    }

    internal static string NormalizeFragment(string fragment)
        => fragment.Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace(' ', '-');

    private static string VisibleText(Markdig.Syntax.Inlines.ContainerInline? inline)
        => inline is null ? "" : string.Concat(inline.Descendants().Select(node =>
            node switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                _ => ""
            }));
}
