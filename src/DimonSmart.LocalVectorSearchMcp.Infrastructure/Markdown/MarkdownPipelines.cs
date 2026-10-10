using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;

/// <summary>All production Markdown parsing and structural validation shares this pipeline.</summary>
internal static class MarkdownPipelines
{
    internal static readonly MarkdownPipeline Tables = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables(new PipeTableOptions { UseGfmRules = true })
        .Build();

    // Historical view is used only to reserve paragraph pointer ordinals
    // previously occupied by a table before pipe table parsing was enabled.
    internal static readonly MarkdownPipeline Historical = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();
}
