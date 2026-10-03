using System.ComponentModel;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Server;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ServerLayerTests
{
    [Fact]
    public void KnowledgeMcpTools_ExposeExpectedWorkbenchSurface()
    {
        var names = typeof(KnowledgeMcpTools).GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                .Cast<McpServerToolAttribute>()
                .SingleOrDefault()?.Name)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "kb_create",
                "kb_delete",
                "kb_list_files",
                "kb_move",
                "kb_outline",
                "kb_patch",
                "kb_read",
                "kb_reindex",
                "kb_search",
                "kb_status"
            ],
            names);
    }

    [Fact]
    public void KnowledgeMcpTools_DescribeSourceReadAndIndexedSearchContracts()
    {
        var readMethod = typeof(KnowledgeMcpTools).GetMethod(
            nameof(KnowledgeMcpTools.ReadAsync))!;
        var searchMethod = typeof(KnowledgeMcpTools).GetMethod(
            nameof(KnowledgeMcpTools.SearchAsync))!;

        var readTool = Assert.Single(
            readMethod.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                .Cast<McpServerToolAttribute>());
        var searchTool = Assert.Single(
            searchMethod.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                .Cast<McpServerToolAttribute>());

        Assert.Equal(typeof(MarkdownSlice), readTool.OutputSchemaType);
        Assert.Equal(typeof(SearchResponse), searchTool.OutputSchemaType);

        var readDescription = Assert.Single(
            readMethod.GetCustomAttributes(typeof(DescriptionAttribute), false)
                .Cast<DescriptionAttribute>()).Description;
        var searchDescription = Assert.Single(
            searchMethod.GetCustomAttributes(typeof(DescriptionAttribute), false)
                .Cast<DescriptionAttribute>()).Description;

        Assert.Contains(
            "current Markdown source",
            readDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Reads indexed Markdown",
            readDescription,
            StringComparison.Ordinal);
        Assert.Contains(
            "indexedSourceHash",
            searchDescription,
            StringComparison.Ordinal);
        Assert.Contains(
            "may temporarily lag",
            searchDescription,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KnowledgeMcpTools_ReadDefaultsMissingPointerToDocument()
    {
        var reader = new EchoSemanticPointerReader();
        var tools = new KnowledgeMcpTools(null!, null!, null!, null!, reader, null!, null!);

        var implicitResult = await tools.ReadAsync(
            new ReadToolRequest("book.md"),
            CancellationToken.None);
        var explicitResult = await tools.ReadAsync(
            new ReadToolRequest("book.md", "document"),
            CancellationToken.None);

        Assert.False(implicitResult.IsError is true);
        Assert.False(explicitResult.IsError is true);
        var implicitRoot = ReadSlice(implicitResult);
        var explicitRoot = ReadSlice(explicitResult);
        Assert.Equal("document", implicitRoot.Pointer);
        Assert.Equal(explicitRoot.Pointer, implicitRoot.Pointer);
    }

    [Fact]
    public async Task KnowledgeMcpTools_ReadReturnsControlledErrorForWhitespacePointer()
    {
        var reader = new EchoSemanticPointerReader();
        var tools = new KnowledgeMcpTools(null!, null!, null!, null!, reader, null!, null!);

        var result = await tools.ReadAsync(
            new ReadToolRequest("book.md", "   "),
            CancellationToken.None);

        Assert.True(result.IsError is true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("Invalid semantic pointer", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeMcpTools_MutationsDeclareStructuredMutationResponse()
    {
        foreach (var methodName in new[]
                 {
                     nameof(KnowledgeMcpTools.CreateAsync),
                     nameof(KnowledgeMcpTools.PatchAsync),
                     nameof(KnowledgeMcpTools.MoveAsync),
                     nameof(KnowledgeMcpTools.DeleteAsync)
                 })
        {
            var method = typeof(KnowledgeMcpTools).GetMethod(methodName)
                ?? throw new Xunit.Sdk.XunitException($"Method '{methodName}' was not found.");
            var attribute = Assert.Single(
                method.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                    .Cast<McpServerToolAttribute>());

            Assert.True(attribute.UseStructuredContent);
            Assert.Equal(typeof(MutationResponse), attribute.OutputSchemaType);
        }
    }

    [Fact]
    public async Task MutationTool_KnownDomainException_ReturnsControlledError()
    {
        var tools = new KnowledgeMcpTools(
            null!,
            null!,
            null!,
            null!,
            null!,
            new ThrowingMutationService(
                new WorkspaceMutationException("expected failure")),
            null!);

        var result = await tools.CreateAsync(
            new CreateToolRequest("test.md", "# Test"),
            CancellationToken.None);

        Assert.True(result.IsError is true);
        Assert.Null(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("expected failure", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KnowledgeMcpTools_OutlineMapsDocumentNotFoundToResourceNotFoundProtocolError()
    {
        var tools = new KnowledgeMcpTools(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new ThrowingNavigationService(
                new DocumentNotFoundException("missing.md")));

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            () => tools.OutlineAsync(
                new OutlineToolRequest("missing.md"),
                CancellationToken.None));

        Assert.Equal(McpErrorCode.ResourceNotFound, exception.ErrorCode);
        Assert.Equal("Document 'missing.md' was not found.", exception.Message);
        var domainException =
            Assert.IsType<DocumentNotFoundException>(exception.InnerException);
        Assert.Equal("missing.md", domainException.Path);
    }

    [Fact]
    public async Task MutationTool_UnexpectedException_IsNotConvertedToControlledError()
    {
        var tools = new KnowledgeMcpTools(
            null!,
            null!,
            null!,
            null!,
            null!,
            new ThrowingMutationService(
                new InvalidOperationException("unexpected failure")),
            null!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.CreateAsync(
                new CreateToolRequest("test.md", "# Test"),
                CancellationToken.None));

        Assert.Equal("unexpected failure", exception.Message);
    }

    [Fact]
    public async Task MutationTool_Success_ReturnsConsistentTextAndStructuredContent()
    {
        var expected = new MutationResponse(
            "test.md",
            "source-hash",
            false,
            null,
            null);
        var tools = new KnowledgeMcpTools(
            null!,
            null!,
            null!,
            null!,
            null!,
            new FixedMutationService(expected),
            null!);

        var result = await tools.CreateAsync(
            new CreateToolRequest("test.md", "# Test"),
            CancellationToken.None);

        Assert.False(result.IsError is true);
        Assert.NotNull(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var fromText = JsonSerializer.Deserialize<MutationResponse>(
            text,
            JsonOptions.Default);
        var fromStructured = result.StructuredContent.Value.Deserialize<MutationResponse>(
            JsonOptions.Default);

        Assert.Equal(expected, fromText);
        Assert.Equal(expected, fromStructured);
    }

    [Fact]
    public void KnownCliExceptionFilter_RecognizesSemanticPointerFormatException()
        => Assert.True(KnownCliExceptionFilter.IsKnown(new SemanticPointerFormatException("bad")));

    [Fact]
    public void KnownCliExceptionFilter_RecognizesDocumentNotFoundException()
        => Assert.True(KnownCliExceptionFilter.IsKnown(new DocumentNotFoundException("missing.md")));

    [Fact]
    public void JsonOptions_SerializesWireEnumsAsLowercase()
    {
        Assert.Equal("\"lexical\"", JsonSerializer.Serialize(SearchMode.Lexical, JsonOptions.Default));
        Assert.Equal("\"changed\"", JsonSerializer.Serialize(ReindexScope.Changed, JsonOptions.Default));
        Assert.Equal(
            SearchMode.Lexical,
            JsonSerializer.Deserialize<SearchMode>("\"LeXiCaL\"", JsonOptions.Default));
        Assert.Equal(
            ReindexScope.Changed,
            JsonSerializer.Deserialize<ReindexScope>("\"ChAnGeD\"", JsonOptions.Default));
        Assert.Equal("\"asset\"", JsonSerializer.Serialize(WorkspaceFileKind.Asset, JsonOptions.Default));
    }

    [Fact]
    public void JsonOptions_SerializesUnicodeWithoutEscaping()
    {
        const string text = "Множество версий и взаимоисключающие";

        var json = JsonSerializer.Serialize(text, JsonOptions.Default);

        Assert.Equal($"\"{text}\"", json);
    }

    [Theory]
    [MemberData(nameof(MaintenanceArguments))]
    public void MaintenanceCommandOptions_ParseDetectsCommands(
        string[] args,
        bool reindex,
        bool status,
        bool force,
        bool isMaintenanceCommand)
    {
        var options = MaintenanceCommandOptions.Parse(args);

        Assert.Equal(reindex, options.Reindex);
        Assert.Equal(status, options.Status);
        Assert.Equal(force, options.Force);
        Assert.Equal(isMaintenanceCommand, options.IsMaintenanceCommand);
    }

    private static MarkdownSlice ReadSlice(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        return JsonSerializer.Deserialize<MarkdownSlice>(text, JsonOptions.Default)
            ?? throw new Xunit.Sdk.XunitException("kb_read returned invalid JSON.");
    }

    private sealed class ThrowingMutationService(Exception exception)
        : IWorkspaceMutationService
    {
        public Task<MutationResponse> PatchAsync(
            PatchRequest request,
            CancellationToken cancellationToken)
            => Task.FromException<MutationResponse>(exception);

        public Task<MutationResponse> CreateAsync(
            string path,
            string markdown,
            CancellationToken cancellationToken)
            => Task.FromException<MutationResponse>(exception);

        public Task<MutationResponse> MoveAsync(
            MoveRequest request,
            CancellationToken cancellationToken)
            => Task.FromException<MutationResponse>(exception);

        public Task<MutationResponse> DeleteAsync(
            DeleteRequest request,
            CancellationToken cancellationToken)
            => Task.FromException<MutationResponse>(exception);
    }

    private sealed class FixedMutationService(MutationResponse response)
        : IWorkspaceMutationService
    {
        public Task<MutationResponse> PatchAsync(
            PatchRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(response);

        public Task<MutationResponse> CreateAsync(
            string path,
            string markdown,
            CancellationToken cancellationToken)
            => Task.FromResult(response);

        public Task<MutationResponse> MoveAsync(
            MoveRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(response);

        public Task<MutationResponse> DeleteAsync(
            DeleteRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private sealed class ThrowingNavigationService(Exception exception)
        : IWorkspaceNavigationService
    {
        public Task<WorkspaceFileList> ListFilesAsync(
            string? pathPrefix,
            string? includeGlob,
            CancellationToken cancellationToken)
            => Task.FromException<WorkspaceFileList>(exception);

        public Task<MarkdownOutline> GetOutlineAsync(
            string path,
            CancellationToken cancellationToken)
            => Task.FromException<MarkdownOutline>(exception);
    }

    private sealed class EchoSemanticPointerReader : ISemanticPointerReader
    {
        public Task<MarkdownSlice> ReadAsync(
            string path,
            SemanticAnchor anchor,
            int maxElements,
            int maxBytes,
            CancellationToken cancellationToken)
            => Task.FromResult(new MarkdownSlice(path, anchor.ToString(), [], "", null, "hash"));
    }

    public static TheoryData<string[], bool, bool, bool, bool> MaintenanceArguments()
        => new()
        {
            { ["--reindex"], true, false, false, true },
            { ["--status"], false, true, false, true },
            { ["--reindex", "--force"], true, false, true, true },
            { ["--force"], false, false, true, false },
            { [], false, false, false, false },
            { ["--reindex", "--status"], true, true, false, true }
        };
}
