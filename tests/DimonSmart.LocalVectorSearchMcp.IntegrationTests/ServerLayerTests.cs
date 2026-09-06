using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Server;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
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
    public void KnownCliExceptionFilter_RecognizesSemanticPointerFormatException()
        => Assert.True(KnownCliExceptionFilter.IsKnown(new SemanticPointerFormatException("bad")));

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
