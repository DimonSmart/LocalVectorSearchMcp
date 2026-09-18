using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol.Client;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class StdioTransportIntegrationTests
{
    private static readonly string[] ExpectedTools =
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
    ];

    [Fact]
    public async Task Stdio_transport_initializes_lists_tools_and_calls_status_without_stdout_contamination()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(temp.Path, cancellationToken);
        var stderr = new ConcurrentQueue<string>();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "local-vector-search-integration",
            Command = "dotnet",
            Arguments = [typeof(KnowledgeMcpTools).Assembly.Location, "--config", configPath],
            WorkingDirectory = temp.Path,
            StandardErrorLines = stderr.Enqueue
        });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        Assert.Equal(
            ExpectedTools,
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var readTool = Assert.Single(tools, tool => tool.Name == "kb_read");
        Assert.Contains("Omit pointer", readTool.Description, StringComparison.Ordinal);
        Assert.Contains("\"document\"", readTool.Description, StringComparison.Ordinal);
        Assert.True(
            HasOptionalSchemaProperty(readTool.JsonSchema, "pointer"),
            $"kb_read schema must expose pointer as optional:{Environment.NewLine}{readTool.JsonSchema}");

        var status = await client.CallToolAsync(
            "kb_status",
            new Dictionary<string, object?>(),
            cancellationToken: cancellationToken);

        Assert.False(status.IsError is true, string.Join(Environment.NewLine, stderr));
    }

    [Fact]
    public async Task Stdio_server_exits_on_input_close_and_emits_no_unsolicited_stdout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(temp.Path, cancellationToken);
        using var process = StartServer(configPath, temp.Path);

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await stderrTask;

        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrEmpty(stdout), $"Unexpected stdout before any MCP request:{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }

    private static bool HasOptionalSchemaProperty(JsonElement schema, string propertyName)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty(propertyName, out _))
            {
                if (!schema.TryGetProperty("required", out var required)
                    || required.ValueKind != JsonValueKind.Array)
                {
                    return true;
                }

                return !required.EnumerateArray().Any(item =>
                    item.ValueKind == JsonValueKind.String
                    && item.GetString() == propertyName);
            }

            foreach (var property in schema.EnumerateObject())
            {
                if (HasOptionalSchemaProperty(property.Value, propertyName))
                {
                    return true;
                }
            }
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                if (HasOptionalSchemaProperty(item, propertyName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Process StartServer(string configPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(typeof(KnowledgeMcpTools).Assembly.Location);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the MCP server process.");
    }

    private static async Task<string> CreateConfigAsync(string root, CancellationToken cancellationToken)
    {
        var storageDirectory = Path.Combine(root, ".local-vector-search-mcp");
        Directory.CreateDirectory(storageDirectory);
        await File.WriteAllTextAsync(Path.Combine(root, "smoke.md"), "# Smoke test\n", cancellationToken);

        var configPath = Path.Combine(root, "tunnel-smoke.yml");
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

    private static string ToYamlPath(string path) => Path.GetFullPath(path).Replace('\\', '/');
}
