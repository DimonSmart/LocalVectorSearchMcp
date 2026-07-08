using System.Text.RegularExpressions;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

public sealed class MarkdownElementParser : IMarkdownElementParser
{
    public IReadOnlyList<MarkdownElement> Parse(MarkdownSourceDocument document)
    {
        var elements = new List<MarkdownElement>
        {
            new(document.KnowledgeBase, document.RelativePath, new SemanticPointer("document"), MarkdownElementKind.Document, "", 1, 1, 0, null)
        };
        var lines = document.Markdown.Split('\n');
        var lineIndex = 0;
        var rootParagraph = 0;
        var rootCode = 0;
        var sectionCounters = new int[6];
        var currentSection = "";
        var currentHeadingPath = new List<string>();
        var paragraphCounts = new Dictionary<string, int>();
        var codeCounts = new Dictionary<string, int>();

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
            if (end > 0)
            {
                var text = string.Join('\n', lines.Take(end + 1)).Trim();
                elements.Add(new MarkdownElement(document.KnowledgeBase, document.RelativePath, new SemanticPointer("frontmatter"), MarkdownElementKind.FrontMatter, text, 1, end + 1, 0, null));
                lineIndex = end + 1;
            }
        }

        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                lineIndex++;
                continue;
            }

            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                sectionCounters[level - 1]++;
                for (var i = level; i < sectionCounters.Length; i++) sectionCounters[i] = 0;
                currentSection = string.Join('.', sectionCounters.Take(level).Where(x => x > 0));
                if (currentHeadingPath.Count >= level) currentHeadingPath.RemoveRange(level - 1, currentHeadingPath.Count - level + 1);
                while (currentHeadingPath.Count < level - 1) currentHeadingPath.Add("");
                if (currentHeadingPath.Count == level - 1) currentHeadingPath.Add(heading.Groups[2].Value.Trim()); else currentHeadingPath[level - 1] = heading.Groups[2].Value.Trim();
                var hp = string.Join(" > ", currentHeadingPath.Where(x => !string.IsNullOrWhiteSpace(x)));
                elements.Add(new MarkdownElement(document.KnowledgeBase, document.RelativePath, new SemanticPointer(currentSection), MarkdownElementKind.Heading, line.Trim(), lineIndex + 1, lineIndex + 1, level, hp));
                lineIndex++;
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var start = lineIndex;
                lineIndex++;
                while (lineIndex < lines.Length && !lines[lineIndex].StartsWith("```", StringComparison.Ordinal)) lineIndex++;
                if (lineIndex < lines.Length) lineIndex++;
                var text = string.Join('\n', lines.Skip(start).Take(lineIndex - start)).TrimEnd();
                var pointer = NextPointer(currentSection, codeCounts, ref rootCode, "code");
                elements.Add(new MarkdownElement(document.KnowledgeBase, document.RelativePath, pointer, MarkdownElementKind.CodeBlock, text, start + 1, lineIndex, 0, HeadingPath(currentHeadingPath)));
                continue;
            }

            var paragraphStart = lineIndex;
            var paragraph = new List<string>();
            while (lineIndex < lines.Length && !string.IsNullOrWhiteSpace(lines[lineIndex]) && !Regex.IsMatch(lines[lineIndex], @"^(#{1,6})\s+") && !lines[lineIndex].StartsWith("```", StringComparison.Ordinal))
            {
                paragraph.Add(lines[lineIndex]);
                lineIndex++;
            }

            var paragraphText = string.Join('\n', paragraph).Trim();
            if (paragraphText.Length > 0)
            {
                var pointer = NextPointer(currentSection, paragraphCounts, ref rootParagraph, "p");
                elements.Add(new MarkdownElement(document.KnowledgeBase, document.RelativePath, pointer, MarkdownElementKind.Paragraph, paragraphText, paragraphStart + 1, lineIndex, 0, HeadingPath(currentHeadingPath)));
            }
        }

        return elements;
    }

    private static SemanticPointer NextPointer(string section, Dictionary<string, int> counts, ref int rootCount, string prefix)
    {
        if (string.IsNullOrEmpty(section))
        {
            rootCount++;
            return new SemanticPointer($"{prefix}{rootCount}");
        }

        counts[section] = counts.GetValueOrDefault(section) + 1;
        return new SemanticPointer($"{section}.{prefix}{counts[section]}");
    }

    private static string? HeadingPath(List<string> headings)
    {
        var value = string.Join(" > ", headings.Where(x => !string.IsNullOrWhiteSpace(x)));
        return value.Length == 0 ? null : value;
    }
}
