using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Server;
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

        var statusTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_status");
        AssertParameterlessToolSchema(
            statusTool.JsonSchema);

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
                "replace_element",
                "replace_section",
                "insert_before",
                "insert_after",
                "delete"
            ],
            kindSchema
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());

        var patchDescription = patchTool.ProtocolTool.Description ?? "";
        Assert.Contains("replace_element", patchDescription, StringComparison.Ordinal);
        Assert.Contains("replace_section", patchDescription, StringComparison.Ordinal);
        Assert.Contains("deprecated alias", patchDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("entire chapter or section", patchDescription, StringComparison.OrdinalIgnoreCase);

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
        var statusResponse =
            JsonSerializer.Deserialize<StatusResponse>(
                ResultText(status),
                JsonOptions.Default);
        Assert.NotNull(statusResponse);
        Assert.False(
            string.IsNullOrWhiteSpace(
                statusResponse.SchemaVersion));
        Assert.NotNull(statusResponse.Project);

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
    public async Task StdioCreateDuplicate_ReturnsActionableControlledError()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken,
            allowWrites: true);
        var stderr = new ConcurrentQueue<string>();
        var transport = CreateTransport(
            "local-vector-search-create-duplicate",
            configPath,
            temp.Path,
            stderr);

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var request = new Dictionary<string, object?>
        {
            ["request"] = new
            {
                path = "duplicate.md",
                markdown = "# Duplicate\n"
            }
        };
        var first = await client.CallToolAsync(
            "kb_create",
            request,
            cancellationToken: cancellationToken);
        var second = await client.CallToolAsync(
            "kb_create",
            request,
            cancellationToken: cancellationToken);

        Assert.False(first.IsError is true);
        Assert.True(second.IsError is true);
        var text = ResultText(second);
        Assert.Contains(
            "already exists",
            text,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "An error occurred invoking 'kb_create'",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StdioMoveAndDeleteWithStaleHash_ReturnActionableControlledErrors()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken,
            allowWrites: true);
        var stderr = new ConcurrentQueue<string>();
        var transport = CreateTransport(
            "local-vector-search-stale-hash",
            configPath,
            temp.Path,
            stderr);

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var moveCreate = await CreateMarkdownAsync(
            client,
            "move-source.md",
            "# Move\n\nOriginal.\n",
            cancellationToken);
        await File.AppendAllTextAsync(
            Path.Combine(temp.Path, "move-source.md"),
            "\nManual edit.\n",
            cancellationToken);
        var move = await client.CallToolAsync(
            "kb_move",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    sourcePath = "move-source.md",
                    targetPath = "move-target.md",
                    expectedSourceHash = moveCreate.SourceHash
                }
            },
            cancellationToken: cancellationToken);

        Assert.True(move.IsError is true);
        Assert.Contains(
            "Document has changed since it was read",
            ResultText(move),
            StringComparison.Ordinal);
        Assert.True(File.Exists(
            Path.Combine(temp.Path, "move-source.md")));
        Assert.False(File.Exists(
            Path.Combine(temp.Path, "move-target.md")));

        var deleteCreate = await CreateMarkdownAsync(
            client,
            "delete-source.md",
            "# Delete\n\nOriginal.\n",
            cancellationToken);
        await File.AppendAllTextAsync(
            Path.Combine(temp.Path, "delete-source.md"),
            "\nManual edit.\n",
            cancellationToken);
        var delete = await client.CallToolAsync(
            "kb_delete",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    path = "delete-source.md",
                    expectedSourceHash = deleteCreate.SourceHash
                }
            },
            cancellationToken: cancellationToken);

        Assert.True(delete.IsError is true);
        Assert.Contains(
            "Document has changed since it was read",
            ResultText(delete),
            StringComparison.Ordinal);
        Assert.True(File.Exists(
            Path.Combine(temp.Path, "delete-source.md")));
    }

    [Fact]
    public async Task StdioMarkdownMutations_WhenWritesDisabled_ReturnControlledErrors()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken);
        var stderr = new ConcurrentQueue<string>();
        var transport = CreateTransport(
            "local-vector-search-writes-disabled",
            configPath,
            temp.Path,
            stderr);

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var calls = new[]
        {
            (
                Name: "kb_create",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            path = "blocked.md",
                            markdown = "# Blocked\n"
                        }
                    }),
            (
                Name: "kb_patch",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            path = "smoke.md",
                            operations = new object[]
                            {
                                new
                                {
                                    kind = "insert_after",
                                    pointer = "document",
                                    markdown = "Blocked."
                                }
                            }
                        }
                    }),
            (
                Name: "kb_move",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            sourcePath = "smoke.md",
                            targetPath = "moved.md",
                            expectedSourceHash = "unused"
                        }
                    }),
            (
                Name: "kb_delete",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            path = "smoke.md",
                            expectedSourceHash = "unused"
                        }
                    })
        };

        foreach (var call in calls)
        {
            var result = await client.CallToolAsync(
                call.Name,
                call.Arguments,
                cancellationToken: cancellationToken);

            Assert.True(result.IsError is true);
            var text = ResultText(result);
            Assert.Contains(
                "Workspace writes are disabled",
                text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "An error occurred invoking",
                text,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StdioMutationWithInvalidPath_ReturnsControlledAccessError()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken,
            allowWrites: true);
        var stderr = new ConcurrentQueue<string>();
        var transport = CreateTransport(
            "local-vector-search-invalid-path",
            configPath,
            temp.Path,
            stderr);

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var result = await client.CallToolAsync(
            "kb_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    path = "../outside.md",
                    markdown = "# Outside\n"
                }
            },
            cancellationToken: cancellationToken);

        Assert.True(result.IsError is true);
        var text = ResultText(result);
        Assert.DoesNotContain(
            "An error occurred invoking",
            text,
            StringComparison.Ordinal);
        Assert.False(File.Exists(
            Path.GetFullPath(Path.Combine(temp.Path, "..", "outside.md"))));
    }

    [Fact]
    public async Task StdioCreate_ReturnsStructuredMutationResponse()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken,
            allowWrites: true);
        var stderr = new ConcurrentQueue<string>();
        var transport = CreateTransport(
            "local-vector-search-structured-mutation",
            configPath,
            temp.Path,
            stderr);

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var result = await client.CallToolAsync(
            "kb_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    path = "structured.md",
                    markdown = "# Structured\n"
                }
            },
            cancellationToken: cancellationToken);

        Assert.False(result.IsError is true);
        Assert.NotNull(result.StructuredContent);
        var structured = result.StructuredContent.Value
            .Deserialize<MutationResponse>(JsonOptions.Default);
        var text = JsonSerializer.Deserialize<MutationResponse>(
            ResultText(result),
            JsonOptions.Default);

        Assert.NotNull(structured);
        Assert.Equal(text, structured);
        Assert.Equal("structured.md", structured.Path);
        Assert.False(structured.IndexSynchronized);
        Assert.Null(structured.IndexError);
        Assert.True(File.Exists(
            Path.Combine(temp.Path, "structured.md")));
    }

    [Fact]
    public async Task StdioMissingDocuments_ReturnDocumentNotFoundAndServerRecovers()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path,
            cancellationToken,
            allowWrites: true);
        var stderr = new ConcurrentQueue<string>();
        var transport = CreateTransport(
            "local-vector-search-document-not-found",
            configPath,
            temp.Path,
            stderr);

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var calls = new[]
        {
            (
                Name: "kb_read",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            path = "missing.md"
                        }
                    }),
            (
                Name: "kb_outline",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            path = "missing.md"
                        }
                    }),
            (
                Name: "kb_patch",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            path = "missing.md",
                            operations = new object[]
                            {
                                new
                                {
                                    kind = "insert_after",
                                    pointer = "document",
                                    markdown = "Text."
                                }
                            }
                        }
                    }),
            (
                Name: "kb_move",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            sourcePath = "missing.md",
                            targetPath = "target.md",
                            expectedSourceHash = "unused"
                        }
                    }),
            (
                Name: "kb_delete",
                Arguments: (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            path = "missing.md",
                            expectedSourceHash = "unused"
                        }
                    })
        };

        foreach (var call in calls)
        {
            var result = await client.CallToolAsync(
                call.Name,
                call.Arguments,
                cancellationToken: cancellationToken);

            Assert.True(result.IsError is true);
            var text = ResultText(result);
            Assert.Equal(
                "Document 'missing.md' was not found.",
                text);
            Assert.DoesNotContain(
                "Pointer 'document' was not found",
                text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "An error occurred invoking",
                text,
                StringComparison.Ordinal);

            var status = await client.CallToolAsync(
                "kb_status",
                new Dictionary<string, object?>(),
                cancellationToken: cancellationToken);
            Assert.False(
                status.IsError is true,
                ResultText(status));

            var read = await client.CallToolAsync(
                "kb_read",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        path = "smoke.md"
                    }
                },
                cancellationToken: cancellationToken);
            Assert.False(
                read.IsError is true,
                ResultText(read));
            Assert.Contains(
                "Smoke test",
                ResultText(read),
                StringComparison.Ordinal);
        }

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

    private static void AssertParameterlessToolSchema(
        JsonElement schema)
    {
        Assert.Equal(
            "object",
            schema.GetProperty("type").GetString());

        if (schema.TryGetProperty(
                "properties",
                out var properties))
        {
            Assert.Equal(
                JsonValueKind.Object,
                properties.ValueKind);
            Assert.False(
                properties.EnumerateObject().Any());
        }

        if (schema.TryGetProperty(
                "required",
                out var required))
        {
            Assert.Equal(
                JsonValueKind.Array,
                required.ValueKind);
            Assert.False(
                required.EnumerateArray().Any());
        }

        Assert.True(
            schema.TryGetProperty(
                "additionalProperties",
                out var additionalProperties),
            $"Parameterless tool schema must declare additionalProperties=false:{Environment.NewLine}{schema}");
        Assert.Equal(
            JsonValueKind.False,
            additionalProperties.ValueKind);
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

    private static StdioClientTransport CreateTransport(
        string name,
        string configPath,
        string workingDirectory,
        ConcurrentQueue<string> stderr)
        => new(
            new StdioClientTransportOptions
            {
                Name = name,
                Command = "dotnet",
                Arguments =
                [
                    typeof(KnowledgeMcpTools).Assembly.Location,
                    "--config",
                    configPath
                ],
                WorkingDirectory = workingDirectory,
                StandardErrorLines = stderr.Enqueue
            });

    private static async Task<MutationResponse> CreateMarkdownAsync(
        McpClient client,
        string path,
        string markdown,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(
            "kb_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    path,
                    markdown
                }
            },
            cancellationToken: cancellationToken);

        Assert.False(result.IsError is true);
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent.Value.Deserialize<MutationResponse>(
                   JsonOptions.Default)
               ?? throw new Xunit.Sdk.XunitException(
                   "kb_create returned invalid structured content.");
    }

    private static string ResultText(CallToolResult result)
        => string.Join(
            Environment.NewLine,
            result.Content
                .OfType<TextContentBlock>()
                .Select(content => content.Text));

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
        CancellationToken cancellationToken,
        bool allowWrites = false)
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
              allowWrites: {allowWrites.ToString().ToLowerInvariant()}
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
