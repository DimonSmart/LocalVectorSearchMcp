using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ComplexMarkdownPatchIntegrationTests
{
    [Fact]
    public async Task ReplaceListItem_WithUnrelatedTwoColumnTable_Succeeds()
    {
        const string source = """
            - A

            | One | Two |
            | --- | --- |
            | a | b |
            """;
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
        var target = Elements(source).Single(element => element.Pointer.Value == "li1");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(target).ToString(), "- Updated")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(source.Replace("- A", "- Updated", StringComparison.Ordinal),
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceListItem_PreservesUnrelatedComplexMarkdown()
    {
        const string source = """
            ---
            title: "Шашлык: учебная QA-книга"
            ---

            # Шашлык без суеты

            Введение: шашлык — это внимание к температуре.

            ## Ингредиенты

            - Мясо: 1 кг
              - Свинина: шея
              - Курица: бедро
            - Лук: 300 г
            - Соль: 15 г

            ## Приготовление

            1. Подготовить мясо.
               - Нарезать кусками.
               - Смешать с луком.
            2. Разжечь угли.
            3. Обжарить и проверить готовность.

            > Советы повара:
            > - Это содержимое цитаты.
            > - Угли должны прогореть.

            ### Время и температура

            | Продукт | Минуты | Маринад |
            | :--- | ---: | :---: |
            | Свинина | 20 | Лук |
            | Грибы | 10 | Соль |
            | Курица | 16 | Лимон |

            ### Короткий код

            ```text
            qa: grill ready
            ```

            ## Подача

            Подавать с овощами и соусом.

            ![Мангал](images/grill.png)

            Конец документа.
            """;
        var expected = source.Replace("- Мясо: 1 кг", "- Мясо: 1.5 кг",
            StringComparison.Ordinal);
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
        var target = Elements(source).Single(element =>
            element.Kind == MarkdownElementKind.ListItem && element.Text.Contains("Мясо: 1 кг",
                StringComparison.Ordinal));

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(target).ToString(), "- Мясо: 1.5 кг")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var elements = Elements(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains(elements, element => element.Kind == MarkdownElementKind.Table);
        Assert.Contains(elements, element => element.Kind == MarkdownElementKind.BlockQuote);
        Assert.Contains(elements, element => element.Kind == MarkdownElementKind.ListItem
            && element.Text.Contains("Мясо: 1.5 кг", StringComparison.Ordinal));
        Assert.Contains(elements, element => element.Kind == MarkdownElementKind.ListItem
            && element.Text.Contains("Свинина: шея", StringComparison.Ordinal));
        Assert.Contains(elements, element => element.Kind == MarkdownElementKind.ListItem
            && element.Text.Contains("Курица: бедро", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PatchOperationKind.InsertBefore, "- Before", "- Before\n- A")]
    [InlineData(PatchOperationKind.InsertAfter, "- After", "- A\n- After")]
    public async Task ListInsertion_WithUnrelatedTable_Succeeds(
        PatchOperationKind operation, string markdown, string expectedItems)
    {
        const string table = "\n\n| One | Two |\n| --- | --- |\n| a | b |\n";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(path, "- A" + table, TestContext.Current.CancellationToken);
        var target = Elements("- A" + table).Single(element => element.Pointer.Value == "li1");

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(operation, SemanticAnchor.FromElement(target).ToString(), markdown)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedItems + table,
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceQuote_WithUnrelatedTable_Succeeds()
    {
        const string source = "> Original\n\n| One | Two |\n| --- | --- |\n| a | b |\n";
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.md");
        await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
        var target = Elements(source).Single(element => element.Kind == MarkdownElementKind.BlockQuote);

        await CreateService(temp.Path).PatchAsync(new PatchRequest("test.md",
            [new PatchOperation(PatchOperationKind.ReplaceElement,
                SemanticAnchor.FromElement(target).ToString(), "> Updated")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(source.Replace("> Original", "> Updated", StringComparison.Ordinal),
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private static IReadOnlyList<MarkdownElement> Elements(string source)
        => new MarkdownElementParser().Parse(new MarkdownSourceDocument(
            "test.md", "test.md", source, "", DateTimeOffset.UtcNow));

    private static WorkspaceMutationService CreateService(string root)
    {
        var config = new LocalVectorSearchMcpConfig
        {
            KnowledgeBase = new KnowledgeBaseConfig { Root = root, AllowWrites = true }
        };
        return new WorkspaceMutationService(config, new KnowledgeBasePathGuard(config),
            new MarkdownDocumentLoader(), new MarkdownElementParser(),
            new ImmediateIndexSynchronizationScheduler(new NoOpSynchronizer()));
    }

    private sealed class NoOpSynchronizer : IWorkspaceIndexSynchronizer
    {
        public Task<bool> ReconcileAsync(string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
