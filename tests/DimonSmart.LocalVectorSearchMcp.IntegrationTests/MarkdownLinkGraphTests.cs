using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Links;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Links;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class MarkdownLinkGraphTests
{
    [Fact]
    public async Task Extracts_distinct_occurrences_and_heading_anchors_using_markdig()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "a.md"), """
            # Overview
            [one](b.md#target)
            [two][ref]
            [ref]: b.md#target

            - [three](b.md#target-1)
            > [missing](missing.md)
            ![image](b.md)
            `[inline code](b.md)`
            ```md
            [fenced](b.md)
            ```
            """, ct);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "b.md"), """
            # Target
            # Target
            [back](a.md#overview)
            """, ct);
        var (config, graph) = Create(directory.Path);
        await graph.SynchronizeAsync(await new MarkdownDocumentLoader().LoadAsync(config.KnowledgeBase, ct), false, ct);

        var outgoing = await graph.QueryAsync(new LinksRequest("a.md", "outgoing",
            IncludeBroken: true), ct);
        Assert.Equal(4, outgoing.Links.Count);
        Assert.Single(outgoing.Nodes);
        Assert.Equal("b.md", outgoing.Nodes[0].Path);
        Assert.Equal(3, outgoing.Links.Count(link => link.Status == "resolved"));
        Assert.Single(outgoing.Links, link => link.Status == "missing_document");
        Assert.All(outgoing.Links, link => Assert.Equal("a.md", link.SourcePath));
        Assert.Contains(outgoing.Links, link => link.Kind == "reference");
        Assert.All(outgoing.Links, link =>
        {
            Assert.True(link.Line > 0);
            Assert.True(link.Column > 0);
            Assert.True(link.Offset >= 0);
            Assert.True(link.Length > 0);
        });

        var incoming = await graph.QueryAsync(new LinksRequest("b.md", "incoming"), ct);
        Assert.Equal(3, incoming.Links.Count);
        Assert.Equal("a.md", Assert.Single(incoming.Nodes).Path);
        var mixed = await graph.QueryAsync(new LinksRequest("a.md", "both", 3), ct);
        Assert.Contains(mixed.Links, link => link.TargetPath == "a.md");
        Assert.Single(mixed.Nodes);
    }

    [Fact]
    public async Task Broken_targets_appear_and_recover_without_reparsing_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "a.md"),
            "[first](new.md#topic) and [second](new.md)\n", ct);
        var (config, graph) = Create(directory.Path);
        var loader = new MarkdownDocumentLoader();
        await graph.SynchronizeAsync(await loader.LoadAsync(config.KnowledgeBase, ct), false, ct);

        var broken = await graph.QueryAsync(new LinksRequest(BrokenOnly: true), ct);
        Assert.Equal(2, broken.Links.Count);
        var inbound = await graph.QueryAsync(new LinksRequest("new.md", "incoming"), ct);
        Assert.Equal(2, inbound.Links.Count);
        Assert.All(inbound.Links, link => Assert.Equal("missing_document", link.Status));

        var target = Path.Combine(directory.Path, "new.md");
        await File.WriteAllTextAsync(target, "# Topic\n", ct);
        await graph.ReconcileAsync("new.md", await loader.LoadFileAsync(config.KnowledgeBase, "new.md", ct), ct);
        Assert.Empty((await graph.QueryAsync(new LinksRequest(BrokenOnly: true), ct)).Links);
        var resolved = await graph.QueryAsync(new LinksRequest("a.md", "outgoing"), ct);
        Assert.Equal(2, resolved.Links.Count);

        await File.WriteAllTextAsync(target, "# Changed\n", ct);
        await graph.ReconcileAsync("new.md", await loader.LoadFileAsync(config.KnowledgeBase, "new.md", ct), ct);
        var anchors = await graph.QueryAsync(new LinksRequest("a.md", "outgoing", IncludeBroken: true), ct);
        Assert.Single(anchors.Links, link => link.Status == "anchor_not_found");

        File.Delete(target);
        await graph.ReconcileAsync("new.md", null, ct);
        Assert.Equal(2, (await graph.QueryAsync(new LinksRequest(BrokenOnly: true), ct)).Links.Count);
    }

    [Fact]
    public async Task Cursor_expires_after_graph_revision_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "a.md");
        await File.WriteAllTextAsync(source, "[1](b.md) [2](b.md) [3](b.md)", ct);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "b.md"), "# B", ct);
        var (config, graph) = Create(directory.Path);
        var loader = new MarkdownDocumentLoader();
        await graph.SynchronizeAsync(await loader.LoadAsync(config.KnowledgeBase, ct), false, ct);

        var first = await graph.QueryAsync(new LinksRequest("a.md", "outgoing", PageSize: 1), ct);
        Assert.NotNull(first.NextCursor);
        var second = await graph.QueryAsync(new LinksRequest("a.md", "outgoing", PageSize: 1,
            Cursor: first.NextCursor), ct);
        Assert.NotEqual(first.Links[0].Id, second.Links[0].Id);

        await File.WriteAllTextAsync(source, "[1](b.md)", ct);
        await graph.ReconcileAsync("a.md", await loader.LoadFileAsync(config.KnowledgeBase, "a.md", ct), ct);
        var error = await Assert.ThrowsAsync<LinkGraphException>(() =>
            graph.QueryAsync(new LinksRequest("a.md", "outgoing", PageSize: 1,
                Cursor: first.NextCursor), ct));
        Assert.Equal("CURSOR_EXPIRED", error.Code);
    }

    [Fact]
    public async Task Reindex_changed_populates_graph_even_when_search_skips_unchanged_content()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "a.md"), "[B](b.md)\n", ct);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "b.md"), "# B\n", ct);
        var (config, graph) = Create(directory.Path);
        var services = SqliteTestServices.Create(config);
        KnowledgeBaseIndexer NewIndexer() => new(config,
            new MarkdownDocumentLoader(), new MarkdownElementParser(),
            new MarkdownChunker(config.Chunking, new EmbeddingTextBuilder()),
            new UnusedProvider(), services.Initializer, services.DocumentStore,
            services.Manifest, linkGraph: graph);

        await services.Initializer.InitializeAsync(ct);
        var oldIndex = new KnowledgeBaseIndexer(config, new MarkdownDocumentLoader(),
            new MarkdownElementParser(), new MarkdownChunker(config.Chunking,
                new EmbeddingTextBuilder()), new UnusedProvider(),
            services.Initializer, services.DocumentStore, services.Manifest);
        await oldIndex.ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);
        var result = await NewIndexer().ReindexAsync(new ReindexRequest(ReindexScope.Changed, false), ct);
        Assert.Equal(2, result.SkippedFiles);
        Assert.Single((await graph.QueryAsync(new LinksRequest("a.md", "outgoing"), ct)).Links);
        Assert.True((await graph.GetStatusAsync(ct)).Ready);
    }

    [Fact]
    public async Task Resolves_safe_relative_percent_and_self_links_but_not_excluded_targets()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "docs"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "docs", "source.md"), """
            # Local
            [self](#local)
            [up](../Other.md)
            [percent](../My%20Document.md)
            [excluded](../excluded.md)
            [unsafe](../../outside.md)
            [encoded](..%2F..%2Foutside.md)
            [absolute](/absolute.md)
            [external](https://example.com/a.md)
            [extensionless](../other)
            """, ct);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Other.md"), "# Other", ct);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "My Document.md"), "# Document", ct);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "excluded.md"), "# Excluded", ct);
        var (initial, _) = Create(directory.Path);
        var config = initial with
        {
            KnowledgeBase = initial.KnowledgeBase with { Exclude = ["excluded.md"] }
        };
        var graph = new SqliteLinkGraphService(new SqliteConnectionFactory(config),
            new KnowledgeBasePathGuard(config));
        await graph.SynchronizeAsync(
            await new MarkdownDocumentLoader().LoadAsync(config.KnowledgeBase, ct), false, ct);

        var links = await graph.QueryAsync(new LinksRequest("docs/source.md", "outgoing",
            IncludeBroken: true), ct);
        Assert.Equal(4, links.Links.Count);
        Assert.Contains(links.Links, item => item.TargetPath == "docs/source.md");
        Assert.Contains(links.Links, item => item.TargetPath == "My Document.md");
        Assert.Single(links.Links, item => item.Status == "target_not_indexed");

        var error = await Assert.ThrowsAsync<LinkGraphException>(() =>
            graph.QueryAsync(new LinksRequest("../escape.md", "incoming"), ct));
        Assert.Equal("INVALID_PATH", error.Code);
    }

    private static (LocalVectorSearchMcpConfig Config, SqliteLinkGraphService Graph) Create(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            Storage = new StorageConfig { Path = Path.Combine(root, "link-index.db") },
            KnowledgeBase = new KnowledgeBaseConfig { Root = root },
            Embedding = new EmbeddingConfig { Provider = "none", Model = "", ApiKey = "", Endpoint = "" },
            Search = new SearchConfig { DefaultMode = SearchMode.Lexical }
        };
        return (config, new SqliteLinkGraphService(new SqliteConnectionFactory(config),
            new KnowledgeBasePathGuard(config)));
    }

    private sealed class UnusedProvider : IEmbeddingProvider
    {
        public Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Lexical-only graph indexing must not embed.");
    }
}
