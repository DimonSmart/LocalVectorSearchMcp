using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class MarkdownTableIntegrationTests
{
    private const string Initial =
        "# Meals\n\nBefore.\n\n| Product | Price | Count |\n" +
        "|:--------|------:|:-----:|\n" +
        "| Pork    | 25    | 2     |\n" +
        "| Chicken | 18    | 3     |\n" +
        "| Veg     | 5     | 4     |\n\nAfter.\n";

    [Fact]
    public void Parser_KeepsTableAtomicAndReservesOldParagraphOrdinals()
    {
        var source = "First.\n\n| A | B |\n|---|---|\n| x | y |\n\nLast.\n";
        var result = Parse(source);
        var content = result.Elements.Where(element =>
            element.Kind is MarkdownElementKind.Paragraph or MarkdownElementKind.Table).ToArray();
        Assert.Equal(new[] { "p1", "t1", "p3" },
            content.Select(element => element.Pointer.Value));
        Assert.Contains("p2", result.ReservedPointers);
        var table = content[1];
        Assert.Equal(table.SelfHash, table.SubtreeHash);
        Assert.Equal("| A | B |\n|---|---|\n| x | y |", table.Text);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public void SourceMap_ReadsGfmCellsAndPreservesOriginalLineEndings(string eol, bool bom)
    {
        var source = "| Name | Link | Qty |" + eol +
            "|:---|:---:|---:|" + eol +
            "| **Chicken** | [Site](https://example.com) | 1 |" + eol +
            "| a\\|b | " + @"x" + " | |";
        var document = Document(source, bom);
        var table = Assert.Single(new MarkdownElementParser().Parse(document),
            item => item.Kind == MarkdownElementKind.Table);
        var model = MarkdownTableSource.Read(source, table);
        Assert.Equal(3, model.Data.ColumnCount);
        Assert.Equal(2, model.Data.RowCount);
        Assert.Equal("Chicken", model.Data.Rows[0].Cells[0]);
        Assert.Equal("Site", model.Data.Rows[0].Cells[1]);
        Assert.Equal("a|b", model.Data.Rows[1].Cells[0]);
        Assert.Equal("", model.Data.Rows[1].Cells[2]);
        Assert.Equal(TableAlignment.Left, model.Data.Columns[0].Alignment);
        Assert.Equal(TableAlignment.Center, model.Data.Columns[1].Alignment);
        Assert.Equal(TableAlignment.Right, model.Data.Columns[2].Alignment);
        Assert.False(model.HasExtraCells);
    }

    [Fact]
    public void Planner_RejectsExtraPhysicalCellsWithoutRewritingSource()
    {
        const string source = "| A | B |\n|---|---|\n| 1 | 2 | hidden |\n";
        var table = Assert.Single(Parse(source).Elements,
            element => element.Kind == MarkdownElementKind.Table);
        var map = MarkdownTableSource.Read(source, table);
        Assert.True(map.HasExtraCells);
        Assert.Throws<WorkspaceMutationException>(() => MarkdownTableEditor.Plan(
            source, map, new TableEditRequest("a.md",
                SemanticAnchor.FromElement(table).ToString(),
                TableEditAction.UpdateCells,
                Updates: [new TableCellUpdate("20", RowIndex: 0, Column: "B")])));
    }

    [Fact]
    public async Task UpdateCells_IsAtomicAndReturnsReusablePointer()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "a.md"), Initial,
            TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        var before = Pointer(Initial);
        var result = await service.EditTableAsync(new TableEditRequest(
            "a.md", before, TableEditAction.UpdateCells,
            Updates:
            [
                new TableCellUpdate("20", Where: new TableWhere("Product", EqualsValue: "Chicken"),
                    Column: "Price"),
                new TableCellUpdate("29", RowIndex: 0, ColumnIndex: 1)
            ]), TestContext.Current.CancellationToken);

        Assert.NotEqual(before, result.Pointer);
        Assert.NotEmpty(result.SourceHash);
        var actual = await File.ReadAllTextAsync(Path.Combine(temp.Path, "a.md"),
            TestContext.Current.CancellationToken);
        Assert.Contains("| Chicken | 20", actual, StringComparison.Ordinal);
        Assert.Contains("| Pork    | 29", actual, StringComparison.Ordinal);
        Assert.Contains("| Veg     | 5", actual, StringComparison.Ordinal);
        Assert.Contains("Before.\n\n", actual, StringComparison.Ordinal);
        Assert.EndsWith("After.\n", actual, StringComparison.Ordinal);

        var next = await service.EditTableAsync(new TableEditRequest(
            "a.md", result.Pointer, TableEditAction.RenameColumn,
            Column: "Price", NewName: "Cost"), TestContext.Current.CancellationToken);
        Assert.NotEqual(result.Pointer, next.Pointer);
        Assert.Contains("Cost", await File.ReadAllTextAsync(
            Path.Combine(temp.Path, "a.md"), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<SemanticAnchorConflictException>(
            () => service.EditTableAsync(new TableEditRequest(
                "a.md", before, TableEditAction.DeleteRow, RowIndex: 0),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AllStructuralActions_PreserveDataAndNeighbors()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, Initial, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        var pointer = Pointer(Initial);

        async Task Apply(TableEditRequest edit)
        {
            var result = await service.EditTableAsync(edit, TestContext.Current.CancellationToken);
            pointer = result.Pointer;
        }

        await Apply(new TableEditRequest("a.md", pointer, TableEditAction.InsertRow,
            Values: ["Lamb", "35", "2"], BeforeRowIndex: 1));
        await Apply(new TableEditRequest("a.md", pointer, TableEditAction.DeleteRow,
            Where: new TableWhere("Product", EqualsValue: "Veg")));
        await Apply(new TableEditRequest("a.md", pointer, TableEditAction.InsertColumn,
            Name: "Note", DefaultValue: "ok", Alignment: TableAlignment.Left));
        await Apply(new TableEditRequest("a.md", pointer, TableEditAction.SetAlignment,
            Column: "Price", Alignment: TableAlignment.Center));
        await Apply(new TableEditRequest("a.md", pointer, TableEditAction.DeleteColumn,
            Column: "Count"));
        await Apply(new TableEditRequest("a.md", pointer, TableEditAction.RenameColumn,
            Column: "Price", NewName: "Cost"));

        var source = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var table = Assert.Single(Parse(source).Elements,
            element => element.Kind == MarkdownElementKind.Table);
        var model = MarkdownTableSource.Read(source, table).Data;
        Assert.Equal(new[] { "Product", "Cost", "Note" },
            model.Columns.Select(col => col.Name));
        Assert.Equal(TableAlignment.Center, model.Columns[1].Alignment);
        Assert.Equal(new[] { "Pork", "Lamb", "Chicken" },
            model.Rows.Select(row => row.Cells[0]));
        Assert.All(model.Rows, row => Assert.Equal("ok", row.Cells[2]));
        Assert.EndsWith("After.\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InsertThenDeleteLastRow_RestoresExactOriginalMarkdown()
    {
        const string original = "# Table\n\n| Product | Mass |\n| --- | ---: |\n| Chicken | 4 |\n\nFollowing paragraph.\n";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        var originalHash = await SourceHash(temp.Path);

        var inserted = await service.EditTableAsync(new TableEditRequest(
            "a.md", Pointer(original), TableEditAction.InsertRow,
            Values: ["Temporary", "123"]), TestContext.Current.CancellationToken);
        var deleted = await service.EditTableAsync(new TableEditRequest(
            "a.md", inserted.Pointer, TableEditAction.DeleteRow,
            Where: new TableWhere("Product", EqualsValue: "Temporary")),
            TestContext.Current.CancellationToken);

        var actual = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(original, actual);
        Assert.Equal(originalHash, deleted.SourceHash);
    }

    [Fact]
    public async Task RepeatedInsertDelete_DoesNotAccumulateWhitespace()
    {
        const string original = "| Product | Mass |\r\n| --- | ---: |\r\n| Chicken | 4 |\r\n\r\nFollowing paragraph.\r\n";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        var pointer = Pointer(original);
        var originalHash = await SourceHash(temp.Path);

        for (var cycle = 0; cycle < 100; cycle++)
        {
            var inserted = await service.EditTableAsync(new TableEditRequest(
                "a.md", pointer, TableEditAction.InsertRow,
                Values: ["Temporary", "123"]), TestContext.Current.CancellationToken);
            var deleted = await service.EditTableAsync(new TableEditRequest(
                "a.md", inserted.Pointer, TableEditAction.DeleteRow,
                Where: new TableWhere("Product", EqualsValue: "Temporary")),
                TestContext.Current.CancellationToken);
            pointer = deleted.Pointer;
            Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal(originalHash, deleted.SourceHash);
        }
    }

    [Fact]
    public async Task InsertThenDeleteLastRow_WithoutFinalEol_RestoresExactMarkdown()
    {
        const string original = "| Product | Mass |\n| --- | ---: |\n| Chicken | 4 |";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        var originalHash = await SourceHash(temp.Path);

        var inserted = await service.EditTableAsync(new TableEditRequest(
            "a.md", Pointer(original), TableEditAction.InsertRow,
            Values: ["Temporary", "123"]), TestContext.Current.CancellationToken);
        var deleted = await service.EditTableAsync(new TableEditRequest(
            "a.md", inserted.Pointer, TableEditAction.DeleteRow,
            RowIndex: 1), TestContext.Current.CancellationToken);

        Assert.Equal(original, await File.ReadAllTextAsync(path,
            TestContext.Current.CancellationToken));
        Assert.Equal(originalHash, deleted.SourceHash);
    }

    [Fact]
    public async Task InvalidBatch_DoesNotModifyFile()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, Initial, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        await Assert.ThrowsAsync<WorkspaceMutationException>(
            () => service.EditTableAsync(new TableEditRequest(
                "a.md", Pointer(Initial), TableEditAction.UpdateCells,
                Updates:
                [
                    new TableCellUpdate("7", RowIndex: 0, ColumnIndex: 1),
                    new TableCellUpdate("8", RowIndex: 0, Column: "Price")
                ]), TestContext.Current.CancellationToken));
        Assert.Equal(Initial,
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadSlice_ReturnsStructuredDataOnlyForAddressedTable()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "a.md"), Initial,
            TestContext.Current.CancellationToken);
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = temp.Path }
        };
        var reader = new SourceMarkdownSliceReader(config,
            new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(), new MarkdownElementParser());
        var cancellation = TestContext.Current.CancellationToken;
        var root = await reader.ReadSliceAsync("a.md",
            SemanticAnchorParser.Parse("document"), 20, 10000, cancellation);
        Assert.Null(root.Table);
        var addressed = await reader.ReadSliceAsync("a.md",
            SemanticAnchorParser.Parse(Pointer(Initial)), 1, 10000, cancellation);
        Assert.NotNull(addressed.Table);
        Assert.Equal(3, addressed.Table.ColumnCount);
        Assert.Equal(3, addressed.Table.RowCount);
        Assert.Equal("Chicken", addressed.Table.Rows[1].Cells[0]);
        Assert.Equal("Price", addressed.Table.Columns[1].Name);
        Assert.Equal(TableAlignment.Right, addressed.Table.Columns[1].Alignment);
        Assert.Equal(Pointer(Initial), addressed.Pointer);
        Assert.Equal(root.SourceHash, addressed.SourceHash);
    }

    [Fact]
    public async Task Patch_RejectsReplacingTableWithParagraph()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, Initial, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        await Assert.ThrowsAsync<WorkspaceMutationException>(() => service.PatchAsync(
            new PatchRequest("a.md",
                [new PatchOperation(PatchOperationKind.ReplaceElement,
                    Pointer(Initial), "Not a table.")]),
            TestContext.Current.CancellationToken));
        Assert.Equal(Initial, await File.ReadAllTextAsync(path,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TextEscaping_NoOpAndMarkdownFormattingRemainSafe()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, Initial, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        var pointer = Pointer(Initial);
        var noOp = await service.EditTableAsync(new TableEditRequest(
            "a.md", pointer, TableEditAction.UpdateCells,
            Updates: [new TableCellUpdate("Chicken", RowIndex: 1, Column: "Product")]),
            TestContext.Current.CancellationToken);
        Assert.Equal(pointer, noOp.Pointer);
        Assert.Equal(Initial, await File.ReadAllTextAsync(path,
            TestContext.Current.CancellationToken));

        var edited = await service.EditTableAsync(new TableEditRequest(
            "a.md", noOp.Pointer, TableEditAction.UpdateCells,
            Updates: [new TableCellUpdate("**Meat** | A\\B",
                RowIndex: 1, Column: "Product")]), TestContext.Current.CancellationToken);
        var source = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var table = Assert.Single(Parse(source).Elements,
            e => e.Kind == MarkdownElementKind.Table);
        var values = MarkdownTableSource.Read(source, table).Data;
        Assert.Equal("**Meat** | A\\B", values.Rows[1].Cells[0]);

        await service.EditTableAsync(new TableEditRequest(
            "a.md", edited.Pointer, TableEditAction.UpdateCells,
            Updates: [new TableCellUpdate("**Bold**", RowIndex: 1,
                Column: "Product", ValueFormat: TableValueFormat.Markdown)]),
            TestContext.Current.CancellationToken);
        source = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("**Bold**", source, StringComparison.Ordinal);
        table = Assert.Single(Parse(source).Elements,
            e => e.Kind == MarkdownElementKind.Table);
        Assert.Equal("Bold", MarkdownTableSource.Read(source, table).Data.Rows[1].Cells[0]);
    }

    [Fact]
    public async Task Patch_TableToTablePreservesNeighbors()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, Initial, TestContext.Current.CancellationToken);
        var service = CreateService(temp.Path);
        const string replacement = "| Product | Price |\n|---|---:|\n| Lamb | 35 |";
        await service.PatchAsync(
            new PatchRequest("a.md",
                [new PatchOperation(PatchOperationKind.ReplaceElement,
                    Pointer(Initial), replacement)]),
            TestContext.Current.CancellationToken);
        var updated = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains(replacement, updated, StringComparison.Ordinal);
        Assert.Contains("Before.\n\n", updated, StringComparison.Ordinal);
        Assert.EndsWith("After.\n", updated, StringComparison.Ordinal);
        var table = Assert.Single(Parse(updated).Elements,
            e => e.Kind == MarkdownElementKind.Table);
        Assert.Equal(2, MarkdownTableSource.Read(updated, table).Data.ColumnCount);
    }

    [Fact]
    public void DeleteColumnToOneColumnRetainsExplicitGfmPipes()
    {
        const string source = "Name | Price\n---|---:\nChicken | 25";
        var table = Assert.Single(Parse(source).Elements,
            e => e.Kind == MarkdownElementKind.Table);
        var map = MarkdownTableSource.Read(source, table);
        var plan = MarkdownTableEditor.Plan(source, map,
            new TableEditRequest("a.md", SemanticAnchor.FromElement(table).ToString(),
                TableEditAction.DeleteColumn, Column: "Price"));
        var updated = Assert.Single(Parse(plan.Source).Elements,
            e => e.Kind == MarkdownElementKind.Table);
        MarkdownTableEditor.ValidateResult(plan, MarkdownTableSource.Read(plan.Source, updated));
        Assert.Contains("| Name |", plan.Source, StringComparison.Ordinal);
    }

    private static MarkdownSourceDocument Document(string source, bool bom = false)
        => new("a.md", "a.md", source, "", DateTimeOffset.UtcNow, bom);

    private static MarkdownParseResult Parse(string source)
        => new MarkdownElementParser().ParseDetailed(Document(source));

    private static string Pointer(string source)
        => SemanticAnchor.FromElement(Assert.Single(Parse(source).Elements,
            element => element.Kind == MarkdownElementKind.Table)).ToString();

    private static async Task<string> SourceHash(string root)
        => (await new MarkdownDocumentLoader().LoadFileAsync(
            new KnowledgeBaseConfig { Root = root }, "a.md",
            TestContext.Current.CancellationToken)).SourceHash;

    private static WorkspaceMutationService CreateService(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = root, AllowWrites = true }
        };
        return new WorkspaceMutationService(config,
            new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(),
            new MarkdownElementParser(),
            new ImmediateIndexSynchronizationScheduler(new NoOpSynchronizer()));
    }

    private sealed class NoOpSynchronizer : IWorkspaceIndexSynchronizer
    {
        public Task<bool> ReconcileAsync(string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
