using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
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
        "kb_move_image",
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
        const string animatedGifBase64 =
            "R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAAgABAAAIBQABAAgIACH5BAEKAAEALAAAAAACAAEAgQD/AAAAAAAAAAAAAAgFAAEACAgAOw==";
        var originalGif = Convert.FromBase64String(animatedGifBase64);
        var gifPath = Path.Combine(temp.Path, "images", "stdio.gif");
        await File.WriteAllBytesAsync(
            gifPath,
            originalGif,
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
        Assert.True(
            HasOptionalSchemaProperty(
                saveTool.JsonSchema,
                "targetPath"));

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

        var moveImageTool = Assert.Single(
            tools,
            tool => tool.Name == "kb_move_image");
        Assert.True(
            HasRequiredSchemaProperty(
                moveImageTool.JsonSchema,
                "request"));
        Assert.True(
            TryFindSchemaProperty(
                moveImageTool.JsonSchema,
                "sourcePath",
                out _));
        Assert.True(
            TryFindSchemaProperty(
                moveImageTool.JsonSchema,
                "targetPath",
                out _));

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
                "replace_subtree",
                "replace_section",
                "delete_section",
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
        Assert.Contains("replace_subtree", patchDescription, StringComparison.Ordinal);
        Assert.Contains("replace_section", patchDescription, StringComparison.Ordinal);
        Assert.Contains("delete_section", patchDescription, StringComparison.Ordinal);
        Assert.Contains("deprecated alias", patchDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section body is preserved", patchDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("complete owned section subtree", patchDescription, StringComparison.OrdinalIgnoreCase);

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

        var readKindResult = await client.CallToolAsync(
            "kb_read",
            new Dictionary<string, object?>
            {
                ["request"] = new { path = "smoke.md" }
            },
            cancellationToken: cancellationToken);
        Assert.False(readKindResult.IsError is true);
        using (var resultJson = JsonDocument.Parse(ResultText(readKindResult)))
        {
            Assert.Equal(
                "heading",
                resultJson.RootElement.GetProperty("elements")[0]
                    .GetProperty("kind").GetString());
        }

        var badTopK = await client.CallToolAsync(
            "kb_search",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    query = "Smoke",
                    mode = "lexical",
                    topK = 0
                }
            },
            cancellationToken: cancellationToken);
        Assert.True(badTopK.IsError is true);
        using (var errorJson = JsonDocument.Parse(ResultText(badTopK)))
        {
            Assert.Equal(
                "INVALID_ARGUMENT",
                errorJson.RootElement.GetProperty("code").GetString());
        }

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
        var imageMetadata = Assert.Single(
            imageResult.Content.OfType<TextContentBlock>());
        var metadata =
            JsonSerializer.Deserialize<ImageLoadResponse>(
                imageMetadata.Text,
                JsonOptions.Default);
        Assert.NotNull(metadata);
        Assert.Equal("images/stdio.png", metadata.Path);
        Assert.Equal("image/png", metadata.MimeType);
        Assert.Null(imageResult.StructuredContent);

        var gifResult = await client.CallToolAsync(
            "kb_load_image",
            new Dictionary<string, object?>
            {
                ["path"] = "images/stdio.gif"
            },
            cancellationToken: cancellationToken);
        Assert.False(gifResult.IsError is true, ResultText(gifResult));
        Assert.Null(gifResult.StructuredContent);
        Assert.Equal(2, gifResult.Content.Count);

        var gifPreview = Assert.IsType<ImageContentBlock>(
            gifResult.Content[0]);
        Assert.Equal("image/png", gifPreview.MimeType);
        var previewBytes = gifPreview.DecodedData.ToArray();
        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
            previewBytes[..8]);
        var decoded = StbImageSharp.ImageResult.FromMemory(
            previewBytes,
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(
            new byte[] { 255, 0, 0, 255 },
            decoded.Data[..4]);

        var gifMetadata = Assert.IsType<TextContentBlock>(
            gifResult.Content[1]);
        var original = JsonSerializer.Deserialize<ImageLoadResponse>(
            gifMetadata.Text,
            JsonOptions.Default);
        Assert.NotNull(original);
        Assert.Equal("images/stdio.gif", original.Path);
        Assert.Equal("image/gif", original.MimeType);
        Assert.Equal(originalGif.LongLength, original.Bytes);
        Assert.Equal(originalGif, await File.ReadAllBytesAsync(
            gifPath,
            cancellationToken));
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
                "INDEX_NOT_READY",
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
        using (var error = JsonDocument.Parse(ResultText(move)))
        {
            Assert.Equal(
                "CONFLICT",
                error.RootElement.GetProperty("code").GetString());
        }
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
        using (var error = JsonDocument.Parse(ResultText(delete)))
        {
            Assert.Equal(
                "CONFLICT",
                error.RootElement.GetProperty("code").GetString());
        }
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
            using var error = JsonDocument.Parse(text);
            Assert.Equal(
                "NOT_FOUND",
                error.RootElement.GetProperty("code").GetString());
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


    [Fact]
    public async Task PublishedMcpSchemasDescribeEveryToolAndMatchStructuredResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        var configPath = await CreateConfigAsync(
            temp.Path, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "outline.md"),
            "# First\n\n## Second\n\n### Third\n",
            cancellationToken);

        var transport = CreateTransport(
            "typed-contracts", configPath, temp.Path,
            new ConcurrentQueue<string>());
        await using var client = await McpClient.CreateAsync(
            transport, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: cancellationToken);
        Assert.Equal(ExpectedTools, tools.Select(tool => tool.Name)
            .OrderBy(name => name, StringComparer.Ordinal));

        // Optional artifact for manual inspection of the actual wire contracts.
        var snapshotPath = Environment.GetEnvironmentVariable(
            "MCP_TOOLS_LIST_SNAPSHOT");
        if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            await File.WriteAllTextAsync(
                snapshotPath,
                JsonSerializer.Serialize(
                    tools.Select(tool => tool.ProtocolTool),
                    JsonOptions.Default),
                cancellationToken);
        }

        foreach (var tool in tools)
        {
            AssertSchemaRefsResolve(tool.JsonSchema);
            if (tool.Name == "kb_load_image")
            {
                Assert.Null(tool.ProtocolTool.OutputSchema);
                continue;
            }

            var output = Assert.IsType<JsonElement>(
                tool.ProtocolTool.OutputSchema);
            AssertSchemaRefsResolve(output);
        }

        var search = Assert.Single(tools, tool => tool.Name == "kb_search");
        AssertStringEnum(
            SchemaProperty(search.JsonSchema, "request", "mode"),
            search.JsonSchema,
            "semantic", "lexical", "hybrid");
        var searchOutput = search.ProtocolTool.OutputSchema!.Value;
        Assert.NotEqual(JsonValueKind.Undefined,
            SchemaProperty(searchOutput, "results", "[]", "readHint").ValueKind);
        Assert.NotEqual(JsonValueKind.Undefined,
            SchemaProperty(searchOutput, "results", "[]", "indexedSourceHash").ValueKind);
        AssertStringEnum(
            SchemaProperty(searchOutput, "results", "[]", "searchMode"),
            searchOutput,
            "semantic", "lexical", "hybrid");

        var reindex = Assert.Single(tools, tool => tool.Name == "kb_reindex");
        AssertStringEnum(
            SchemaProperty(reindex.JsonSchema, "request", "scope"),
            reindex.JsonSchema, "changed", "all");
        var reindexOutput = reindex.ProtocolTool.OutputSchema!.Value;
        Assert.NotEqual(JsonValueKind.Undefined,
            SchemaProperty(reindexOutput, "current", "startedAtUtc").ValueKind);
        Assert.NotEqual(JsonValueKind.Undefined,
            SchemaProperty(reindexOutput, "current", "isDestructiveRebuild").ValueKind);

        var patch = Assert.Single(tools, tool => tool.Name == "kb_patch");
        AssertStringEnum(
            SchemaProperty(patch.JsonSchema,
                "request", "operations", "[]", "kind"),
            patch.JsonSchema,
            "replace", "replace_element", "replace_subtree",
            "replace_section", "delete_section", "insert_before",
            "insert_after", "delete");

        var files = Assert.Single(tools, tool => tool.Name == "kb_list_files");
        var filesOutput = files.ProtocolTool.OutputSchema!.Value;
        AssertStringEnum(
            SchemaProperty(filesOutput, "files", "[]", "kind"),
            filesOutput, "markdown", "asset");

        var read = Assert.Single(tools, tool => tool.Name == "kb_read");
        var readOutput = read.ProtocolTool.OutputSchema!.Value;
        AssertStringEnum(
            SchemaProperty(readOutput, "elements", "[]", "kind"),
            readOutput,
            "document", "front_matter", "heading", "paragraph",
            "code_block", "list_item", "table", "block_quote");

        var status = Assert.Single(tools, tool => tool.Name == "kb_status");
        AssertParameterlessToolSchema(status.JsonSchema);
        var statusOutput = status.ProtocolTool.OutputSchema!.Value;
        Assert.NotEqual(JsonValueKind.Undefined,
            SchemaProperty(statusOutput, "indexing", "last", "outcome").ValueKind);
        Assert.NotEqual(JsonValueKind.Undefined,
            SchemaProperty(statusOutput, "compatibility", "isCompatible").ValueKind);

        var outline = Assert.Single(tools, tool => tool.Name == "kb_outline");
        var outlineOutput = outline.ProtocolTool.OutputSchema!.Value;
        Assert.NotEqual(JsonValueKind.Undefined,
            SchemaProperty(outlineOutput,
                "headings", "[]", "children", "[]", "children").ValueKind);

        var calls = new (string Name, IReadOnlyDictionary<string, object?> Args)[]
        {
            ("kb_status", new Dictionary<string, object?>()),
            ("kb_outline", new Dictionary<string, object?>
            {
                ["request"] = new { path = "outline.md" }
            }),
            ("kb_list_files", new Dictionary<string, object?>
            {
                ["request"] = new { }
            }),
            ("kb_read", new Dictionary<string, object?>
            {
                ["request"] = new { path = "outline.md" }
            }),
            ("kb_reindex", new Dictionary<string, object?>
            {
                ["request"] = new { scope = "changed" }
            })
        };

        foreach (var call in calls)
        {
            var tool = Assert.Single(tools, tool => tool.Name == call.Name);
            var result = await client.CallToolAsync(
                call.Name, call.Args, cancellationToken: cancellationToken);
            Assert.False(result.IsError is true, ResultText(result));
            var structured = Assert.IsType<JsonElement>(
                result.StructuredContent);
            using var text = JsonDocument.Parse(ResultText(result));
            Assert.True(JsonElement.DeepEquals(
                text.RootElement, structured));
            AssertMatchesSchema(
                structured, tool.ProtocolTool.OutputSchema!.Value,
                tool.ProtocolTool.OutputSchema!.Value);

            if (call.Name == "kb_list_files")
            {
                var kinds = structured.GetProperty("files")
                    .EnumerateArray()
                    .Select(file => file.GetProperty("kind").GetString())
                    .ToArray();
                Assert.Contains("markdown", kinds);
                Assert.Contains("asset", kinds);
            }

            if (call.Name == "kb_outline")
            {
                var first = structured.GetProperty("headings")[0];
                var second = first.GetProperty("children")[0];
                var third = second.GetProperty("children")[0];
                Assert.Equal(1, first.GetProperty("level").GetInt32());
                Assert.Equal(2, second.GetProperty("level").GetInt32());
                Assert.Equal(3, third.GetProperty("level").GetInt32());
            }
        }

        var error = await client.CallToolAsync(
            "kb_outline",
            new Dictionary<string, object?>
            {
                ["request"] = new { path = "missing.md" }
            },
            cancellationToken: cancellationToken);
        Assert.True(error.IsError is true);
        Assert.Null(error.StructuredContent);
        using var errorJson = JsonDocument.Parse(ResultText(error));
        Assert.Equal("NOT_FOUND",
            errorJson.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("\"semantic\"", SearchMode.Semantic)]
    [InlineData("\"LEXICAL\"", SearchMode.Lexical)]
    [InlineData("\"hybrid\"", SearchMode.Hybrid)]
    public void SearchModeWireFormatRetainsCaseInsensitiveStrings(
        string json, SearchMode expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<SearchMode>(
            json, JsonOptions.Default));
        Assert.Equal(expected, JsonSerializer.Deserialize<SearchMode>(json));
        Assert.Equal(expected.ToWireValue(),
            JsonSerializer.Serialize(expected, JsonOptions.Default).Trim('"'));
    }

    [Theory]
    [InlineData("\"changed\"", ReindexScope.Changed)]
    [InlineData("\"ALL\"", ReindexScope.All)]
    public void ReindexScopeWireFormatRetainsCaseInsensitiveStrings(
        string json, ReindexScope expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<ReindexScope>(
            json, JsonOptions.Default));
        Assert.Equal(expected, JsonSerializer.Deserialize<ReindexScope>(json));
        Assert.Equal(expected.ToWireValue(),
            JsonSerializer.Serialize(expected, JsonOptions.Default).Trim('"'));
    }

    [Theory]
    [InlineData("\"markdown\"", WorkspaceFileKind.Markdown)]
    [InlineData("\"ASSET\"", WorkspaceFileKind.Asset)]
    public void FileKindWireFormatRetainsCaseInsensitiveStrings(
        string json, WorkspaceFileKind expected)
    {
        Assert.Equal(expected,
            JsonSerializer.Deserialize<WorkspaceFileKind>(json));
        Assert.Equal(expected,
            JsonSerializer.Deserialize<WorkspaceFileKind>(
                json, JsonOptions.Default));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"bogus\"")]
    public void StrictEnumConvertersRejectUnsupportedWireValues(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SearchMode>(json));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ReindexScope>(json));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<WorkspaceFileKind>(json));
    }

    private static void AssertStringEnum(
        JsonElement schema, JsonElement document, params string[] expected)
    {
        schema = ResolveSchema(schema, document);
        Assert.Equal("string",
            schema.GetProperty("type").GetString());
        Assert.Equal(expected,
            schema.GetProperty("enum").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
    }

    private static JsonElement SchemaProperty(
        JsonElement document, params string[] path)
    {
        var schema = document;
        foreach (var segment in path)
        {
            schema = ResolveSchema(schema, document);
            schema = segment == "[]"
                ? schema.GetProperty("items")
                : schema.GetProperty("properties").GetProperty(segment);
        }

        return ResolveSchema(schema, document);
    }

    private static JsonElement ResolveSchema(
        JsonElement schema, JsonElement document)
    {
        for (var depth = 0; depth < 32; depth++)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return schema;
            }

            if (schema.TryGetProperty("$ref", out var reference))
            {
                schema = FollowSchemaRef(document, reference.GetString()!);
                continue;
            }

            if (schema.TryGetProperty("anyOf", out var alternatives)
                || schema.TryGetProperty("oneOf", out alternatives))
            {
                schema = alternatives.EnumerateArray()
                    .First(item => !IsNullSchema(item, document));
                continue;
            }

            if (schema.TryGetProperty("allOf", out var all))
            {
                schema = all[0];
                continue;
            }

            return schema;
        }

        throw new Xunit.Sdk.XunitException("Schema reference cycle.");
    }

    private static bool IsNullSchema(
        JsonElement schema, JsonElement document)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return IsNullSchema(
                FollowSchemaRef(document, reference.GetString()!), document);
        }

        return schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "null";
    }

    private static JsonElement FollowSchemaRef(
        JsonElement document, string reference)
    {
        Assert.StartsWith("#/", reference, StringComparison.Ordinal);
        var current = document;
        foreach (var token in reference[2..].Split('/'))
        {
            current = current.GetProperty(
                token.Replace("~1", "/").Replace("~0", "~"));
        }

        return current;
    }

    private static void AssertSchemaRefsResolve(JsonElement schema)
    {
        static void Visit(JsonElement node, JsonElement root)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray())
                {
                    Visit(child, root);
                }
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    if (property.Name == "$ref")
                    {
                        Assert.NotEqual(JsonValueKind.Undefined,
                            FollowSchemaRef(root, property.Value.GetString()!).ValueKind);
                    }
                    else
                    {
                        Visit(property.Value, root);
                    }
                }
            }
        }

        Visit(schema, schema);
    }

    private static void AssertMatchesSchema(
        JsonElement value, JsonElement schema, JsonElement document)
    {
        if (value.ValueKind == JsonValueKind.Null
            && schema.ValueKind == JsonValueKind.Object
            && (schema.TryGetProperty("anyOf", out var nullableAlternatives)
                || schema.TryGetProperty("oneOf", out nullableAlternatives)))
        {
            Assert.Contains(nullableAlternatives.EnumerateArray(),
                alternative => IsNullSchema(alternative, document));
            return;
        }

        schema = ResolveSchema(schema, document);
        if (schema.TryGetProperty("type", out var type))
        {
            IEnumerable<string> allowed = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Select(element => element.GetString()!)
                : [type.GetString()!];
            var actualType = JsonType(value);
            if (actualType == "integer")
            {
                Assert.True(allowed.Contains("integer") || allowed.Contains("number"));
            }
            else
            {
                Assert.Contains(actualType, allowed);
            }
        }

        if (schema.TryGetProperty("enum", out var choices))
        {
            Assert.Contains(choices.EnumerateArray(),
                item => JsonElement.DeepEquals(item, value));
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var item in required.EnumerateArray())
                {
                    Assert.True(value.TryGetProperty(item.GetString()!, out _),
                        $"Required field missing: {item.GetString()}");
                }
            }

            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateObject())
                {
                    if (value.TryGetProperty(property.Name, out var actual))
                    {
                        AssertMatchesSchema(actual, property.Value, document);
                    }
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array
            && schema.TryGetProperty("items", out var items))
        {
            foreach (var item in value.EnumerateArray())
            {
                AssertMatchesSchema(item, items, document);
            }
        }
    }

    private static string JsonType(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => element.TryGetInt64(out _) ? "integer" : "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => throw new Xunit.Sdk.XunitException(
                "Unsupported JSON value.")
        };

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


    [Fact]
    public async Task StdioListAndQuote_AreAddressableWithoutExposingQuoteChildren()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TemporaryDirectory();
        const string source = "- Parent\n  - Child\n- Tail\n\n> - Quoted\n";
        var filePath = Path.Combine(temp.Path, "semantic.md");
        await File.WriteAllTextAsync(filePath, source, cancellationToken);
        var configPath = await CreateConfigAsync(temp.Path, cancellationToken, allowWrites: true);
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "list-quote-stdio-regression",
            Command = "dotnet",
            Arguments = [typeof(KnowledgeMcpTools).Assembly.Location, "--config", configPath],
            WorkingDirectory = temp.Path
        });

        await using var client = await McpClient.CreateAsync(
            transport, cancellationToken: cancellationToken);
        var read = await client.CallToolAsync(
            "kb_read",
            new Dictionary<string, object?>
            {
                ["request"] = new { path = "semantic.md" }
            },
            cancellationToken: cancellationToken);
        Assert.False(read.IsError is true, ResultText(read));

        string childPointer;
        string quotePointer;
        using (var json = JsonDocument.Parse(ResultText(read)))
        {
            var elements = json.RootElement.GetProperty("elements").EnumerateArray().ToArray();
            Assert.Equal(4, elements.Length);
            Assert.Equal(["list_item", "list_item", "list_item", "block_quote"],
                elements.Select(e => e.GetProperty("kind").GetString()));
            Assert.StartsWith("li1.li1~", elements[1].GetProperty("pointer").GetString());
            Assert.StartsWith("q1~", elements[3].GetProperty("pointer").GetString());
            childPointer = elements[1].GetProperty("pointer").GetString()!;
            quotePointer = elements[3].GetProperty("pointer").GetString()!;
        }

        var delete = await client.CallToolAsync(
            "kb_patch",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    path = "semantic.md",
                    operations = new[] { new { kind = "delete", pointer = childPointer } }
                }
            },
            cancellationToken: cancellationToken);
        Assert.False(delete.IsError is true, ResultText(delete));
        Assert.Equal("- Parent\n- Tail\n\n> - Quoted\n",
            await File.ReadAllTextAsync(filePath, cancellationToken));

        var replace = await client.CallToolAsync(
            "kb_patch",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    path = "semantic.md",
                    operations = new[]
                    {
                        new { kind = "replace_element", pointer = quotePointer, markdown = "> New" }
                    }
                }
            },
            cancellationToken: cancellationToken);
        Assert.False(replace.IsError is true, ResultText(replace));
        Assert.Equal("- Parent\n- Tail\n\n> New\n",
            await File.ReadAllTextAsync(filePath, cancellationToken));

        var invalid = await client.CallToolAsync(
            "kb_patch",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    path = "semantic.md",
                    operations = new[]
                    {
                        new { kind = "delete", pointer = "q1.li1~0123456789abcdef~fedcba9876543210" }
                    }
                }
            },
            cancellationToken: cancellationToken);
        Assert.True(invalid.IsError is true);
        Assert.Equal("- Parent\n- Tail\n\n> New\n",
            await File.ReadAllTextAsync(filePath, cancellationToken));
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
