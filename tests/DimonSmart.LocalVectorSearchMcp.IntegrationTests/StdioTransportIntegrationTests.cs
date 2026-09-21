using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class StdioTransportIntegrationTests
{
    private static readonly string[] ExpectedTools =
    [
        "kb_create",
        "kb_delete",
        "kb_delete_image",
        "kb_list_files",
        "kb_list_images",
        "kb_load_image",
        "kb_move",
        "kb_outline",
        "kb_patch",
        "kb_read",
        "kb_reindex",
        "kb_save_image",
        "kb_search",
        "kb_status"
    ];

    [Fact]
    public async Task StdioTransportDiscoversProductionImageToolsAndReturnsImageContent()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken);
        var stderr = new ConcurrentQueue<string>();

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "local-vector-search-integration",
                Command = "dotnet",
                Arguments =
                [
                    typeof(KnowledgeMcpTools).Assembly.Location,
                    "--config",
                    configPath
                ],
                WorkingDirectory = temp.Path,
                StandardErrorLines = stderr.Enqueue
            });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: cancellationToken);
        Assert.Equal(
            ExpectedTools,
            tools
                .Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        Assert.DoesNotContain(
            tools,
            tool => tool.Name.StartsWith(
                "debug_",
                StringComparison.Ordinal));

        var saveTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_save_image");
        Assert.Equal(
            """["file"]""",
            saveTool.ProtocolTool.Meta?["openai/fileParams"]
                ?.ToJsonString());
        Assert.True(
            HasRequiredSchemaProperty(
                saveTool.JsonSchema,
                "file"));
        Assert.True(
            HasRequiredSchemaProperty(
                saveTool.JsonSchema,
                "download_url"),
            $"kb_save_image schema must require download_url:{Environment.NewLine}{saveTool.JsonSchema}");
        Assert.True(
            HasRequiredSchemaProperty(
                saveTool.JsonSchema,
                "file_id"),
            $"kb_save_image schema must require file_id:{Environment.NewLine}{saveTool.JsonSchema}");
        Assert.True(
            HasOptionalSchemaProperty(
                saveTool.JsonSchema,
                "mime_type"));
        Assert.True(
            HasOptionalSchemaProperty(
                saveTool.JsonSchema,
                "file_name"));
        Assert.True(
            HasOptionalSchemaProperty(
                saveTool.JsonSchema,
                "fileName"));
        Assert.True(
            HasOptionalSchemaProperty(
                saveTool.JsonSchema,
                "altText"));

        var listTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_list_images");
        Assert.True(
            HasOptionalSchemaProperty(
                listTool.JsonSchema,
                "cursor"));
        Assert.True(
            HasOptionalSchemaProperty(
                listTool.JsonSchema,
                "pageSize"));

        var loadTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_load_image");
        Assert.True(
            HasRequiredSchemaProperty(
                loadTool.JsonSchema,
                "path"));

        var deleteTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_delete_image");
        Assert.True(
            HasRequiredSchemaProperty(
                deleteTool.JsonSchema,
                "path"));

        var readTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_read");
        Assert.True(
            HasOptionalSchemaProperty(
                readTool.JsonSchema,
                "pointer"));

        var patchTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_patch");
        Assert.False(
            TryFindSchemaProperty(
                patchTool.JsonSchema,
                "expectedSourceHash",
                out _));

        var operationsSchema = FindSchemaProperty(
            patchTool.JsonSchema,
            "operations");
        Assert.Equal(
            "array",
            operationsSchema.GetProperty("type").GetString());
        Assert.True(
            operationsSchema.TryGetProperty(
                "items",
                out var operationItemSchema));

        var kindSchema = FindSchemaProperty(
            operationItemSchema,
            "kind");
        Assert.Equal(
            "string",
            kindSchema.GetProperty("type").GetString());
        Assert.Equal(
            [
                "replace",
                "insert_before",
                "insert_after",
                "delete"
            ],
            kindSchema
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());

        var malformedSave = await client.CallToolAsync(
            "kb_save_image",
            new Dictionary<string, object?>
            {
                ["file"] = new
                {
                    file_id = "missing-download-url"
                }
            },
            cancellationToken: cancellationToken);
        Assert.True(malformedSave.IsError is true);
        Assert.DoesNotContain(
            stderr,
            line => line.Contains(
                "threw an unhandled exception",
                StringComparison.Ordinal));

        var status = await client.CallToolAsync(
            "kb_status",
            new Dictionary<string, object?>(),
            cancellationToken: cancellationToken);
        Assert.False(
            status.IsError is true,
            string.Join(Environment.NewLine, stderr));

        var imageResult = await client.CallToolAsync(
            "kb_load_image",
            new Dictionary<string, object?>
            {
                ["path"] = "images/stdio.png"
            },
            cancellationToken: cancellationToken);

        Assert.False(
            imageResult.IsError is true,
            string.Join(Environment.NewLine, stderr));
        var image = Assert.Single(
            imageResult.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/png", image.MimeType);
        var imageBytes = image.DecodedData.ToArray();
        Assert.True(imageBytes.Length >= 8);
        Assert.Equal(
            new byte[]
            {
                0x89, 0x50, 0x4e, 0x47,
                0x0d, 0x0a, 0x1a, 0x0a
            },
            imageBytes[..8]);
        Assert.NotNull(imageResult.StructuredContent);
    }

    [Fact]
    public async Task SearchWithEmptyIndexReturnsControlledToolError()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken);
        var stderr = new ConcurrentQueue<string>();

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "local-vector-search-empty-index",
                Command = "dotnet",
                Arguments =
                [
                    typeof(KnowledgeMcpTools).Assembly.Location,
                    "--config",
                    configPath
                ],
                WorkingDirectory = temp.Path,
                StandardErrorLines = stderr.Enqueue
            });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);
        var result = await client.CallToolAsync(
            "kb_search",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    query = "Smoke",
                    mode = "lexical"
                }
            },
            cancellationToken: cancellationToken);

        Assert.True(result.IsError is true);
        var text = string.Join(
            Environment.NewLine,
            result.Content
                .OfType<TextContentBlock>()
                .Select(content => content.Text));
        Assert.True(
            text.Contains(
                "Run kb_reindex first.",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            stderr,
            line => line.Contains(
                "threw an unhandled exception",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task StdioPatchReturnsExpectedDomainErrorToClient()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken);
        var stderr = new ConcurrentQueue<string>();

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name =
                    "local-vector-search-patch-error-integration",
                Command = "dotnet",
                Arguments =
                [
                    typeof(KnowledgeMcpTools).Assembly.Location,
                    "--config",
                    configPath
                ],
                WorkingDirectory = temp.Path,
                StandardErrorLines = stderr.Enqueue
            });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var result = await client.CallToolAsync(
            "kb_patch",
            new Dictionary<string, object?>
            {
                ["request"] =
                    new Dictionary<string, object?>
                    {
                        ["path"] = "smoke.md",
                        ["operations"] =
                            new object[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["kind"] = "delete",
                                    ["pointer"] = "1"
                                }
                            }
                    }
            },
            cancellationToken: cancellationToken);

        Assert.True(result.IsError is true);
        Assert.DoesNotContain(
            stderr,
            line => line.Contains(
                "threw an unhandled exception",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task StdioServerExitsOnInputCloseWithoutUnsolicitedStdout()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken);
        using var process = StartServer(
            configPath,
            temp.Path);

        var stderrTask =
            process.StandardError.ReadToEndAsync(
                cancellationToken);
        process.StandardInput.Close();

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);

        var stdout =
            await process.StandardOutput.ReadToEndAsync(
                cancellationToken);
        var stderr = await stderrTask;

        Assert.Equal(0, process.ExitCode);
        Assert.True(
            string.IsNullOrEmpty(stdout),
            $"Unexpected stdout before any MCP request:{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }

    private static JsonElement FindSchemaProperty(
        JsonElement schema,
        string propertyName)
    {
        if (TryFindSchemaProperty(
                schema,
                propertyName,
                out var propertySchema))
        {
            return propertySchema;
        }

        throw new Xunit.Sdk.XunitException(
            $"Schema property '{propertyName}' was not found.");
    }

    private static bool TryFindSchemaProperty(
        JsonElement schema,
        string propertyName,
        out JsonElement propertySchema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty(
                    "properties",
                    out var properties)
                && properties.ValueKind ==
                JsonValueKind.Object
                && properties.TryGetProperty(
                    propertyName,
                    out propertySchema))
            {
                return true;
            }

            foreach (var property
                     in schema.EnumerateObject())
            {
                if (TryFindSchemaProperty(
                        property.Value,
                        propertyName,
                        out propertySchema))
                {
                    return true;
                }
            }
        }
        else if (schema.ValueKind ==
                 JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                if (TryFindSchemaProperty(
                        item,
                        propertyName,
                        out propertySchema))
                {
                    return true;
                }
            }
        }

        propertySchema = default;
        return false;
    }

    private static bool HasRequiredSchemaProperty(
        JsonElement schema,
        string propertyName)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty(
                    "properties",
                    out var properties)
                && properties.ValueKind ==
                JsonValueKind.Object
                && properties.TryGetProperty(
                    propertyName,
                    out _)
                && schema.TryGetProperty(
                    "required",
                    out var required)
                && required.ValueKind ==
                JsonValueKind.Array
                && required.EnumerateArray()
                    .Any(item =>
                        item.ValueKind ==
                        JsonValueKind.String
                        && item.GetString() ==
                        propertyName))
            {
                return true;
            }

            foreach (var property
                     in schema.EnumerateObject())
            {
                if (HasRequiredSchemaProperty(
                        property.Value,
                        propertyName))
                {
                    return true;
                }
            }
        }
        else if (schema.ValueKind ==
                 JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                if (HasRequiredSchemaProperty(
                        item,
                        propertyName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasOptionalSchemaProperty(
        JsonElement schema,
        string propertyName)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty(
                    "properties",
                    out var properties)
                && properties.ValueKind ==
                JsonValueKind.Object
                && properties.TryGetProperty(
                    propertyName,
                    out _))
            {
                if (!schema.TryGetProperty(
                        "required",
                        out var required)
                    || required.ValueKind !=
                    JsonValueKind.Array)
                {
                    return true;
                }

                return !required
                    .EnumerateArray()
                    .Any(item =>
                        item.ValueKind ==
                        JsonValueKind.String
                        && item.GetString() ==
                        propertyName);
            }

            foreach (var property
                     in schema.EnumerateObject())
            {
                if (HasOptionalSchemaProperty(
                        property.Value,
                        propertyName))
                {
                    return true;
                }
            }
        }
        else if (schema.ValueKind ==
                 JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                if (HasOptionalSchemaProperty(
                        item,
                        propertyName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Process StartServer(
        string configPath,
        string workingDirectory)
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
        startInfo.ArgumentList.Add(
            typeof(KnowledgeMcpTools).Assembly.Location);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start the MCP server process.");
    }

    private static async Task<string> CreateConfigAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var storageDirectory = Path.Combine(
            root,
            ".local-vector-search-mcp");
        Directory.CreateDirectory(storageDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(root, "smoke.md"),
            "# Smoke test\n",
            cancellationToken);

        var imagesDirectory =
            Path.Combine(root, "images");
        Directory.CreateDirectory(imagesDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(imagesDirectory, "stdio.png"),
            [
                0x89, 0x50, 0x4e, 0x47,
                0x0d, 0x0a, 0x1a, 0x0a,
                0x00, 0x01
            ],
            cancellationToken);

        var configPath = Path.Combine(
            root,
            "tunnel-smoke.yml");
        var yaml = $"""
            knowledgeBase:
              root: "{ToYamlPath(root)}"
              allowWrites: false
              watchFiles: false
            storage:
              path: "{ToYamlPath(Path.Combine(storageDirectory, "index.db"))}"
            """;
        await File.WriteAllTextAsync(
            configPath,
            yaml,
            cancellationToken);
        return configPath;
    }

    private static string ToYamlPath(string path)
        => Path.GetFullPath(path).Replace('\\', '/');
}
