using System.Collections.Concurrent;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class FileTransferDiagnosticStdioIntegrationTests
{
    [Fact]
    public async Task Malformed_file_parameter_returns_controlled_tool_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(temp.Path, cancellationToken);
        var stderr = new ConcurrentQueue<string>();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "local-vector-search-file-transfer-error",
            Command = "dotnet",
            Arguments = [typeof(KnowledgeMcpTools).Assembly.Location, "--config", configPath],
            WorkingDirectory = temp.Path,
            StandardErrorLines = stderr.Enqueue
        });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var result = await client.CallToolAsync(
            "debug_receive_file",
            new Dictionary<string, object?>
            {
                ["file"] = "not-an-openai-file-object"
            },
            cancellationToken: cancellationToken);

        var errorText = string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        Assert.True(result.IsError is true, errorText);
        Assert.Contains("Invalid OpenAI file parameter", errorText, StringComparison.Ordinal);
        Assert.Contains("received String", errorText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            stderr,
            line => line.Contains("threw an unhandled exception", StringComparison.Ordinal));
    }

    private static async Task<string> CreateConfigAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var storageDirectory = Path.Combine(root, ".local-vector-search-mcp");
        Directory.CreateDirectory(storageDirectory);

        var configPath = Path.Combine(root, "file-transfer-smoke.yml");
        var yaml = $"""
            knowledgeBase:
              root: "{ToYamlPath(root)}"
              allowWrites: false
              watchFiles: false
            storage:
              path: "{ToYamlPath(Path.Combine(storageDirectory, "index.db"))}"
            """;

        await File.WriteAllTextAsync(configPath, yaml, cancellationToken);
        return configPath;
    }

    private static string ToYamlPath(string path)
        => Path.GetFullPath(path).Replace('\\', '/');
}
