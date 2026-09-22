using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;
using DimonSmart.LocalVectorSearchMcp.Server;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class ReindexStdioIntegrationTests
{
    [Fact]
    public async Task SlowReindex_DoesNotBlockStdioMcpRequests()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        using var temp = new TemporaryDirectory();
        await using var embeddings =
            new BlockingEmbeddingEndpoint(cancellationToken);
        var configPath = await CreateConfigAsync(
            temp.Path,
            embeddings.Port,
            cancellationToken);
        var stderr = new ConcurrentQueue<string>();

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "local-vector-search-background-reindex",
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

        var first = await client.CallToolAsync(
            "kb_reindex",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    scope = "changed",
                    force = false
                }
            },
            cancellationToken: cancellationToken);

        Assert.False(first.IsError is true);
        var start = JsonSerializer.Deserialize<ReindexStartResponse>(
            ResultText(first),
            JsonOptions.Default);
        Assert.NotNull(start);
        Assert.True(start.Started);

        await embeddings.RequestReceived.Task.WaitAsync(cancellationToken);

        var statusResult = await client.CallToolAsync(
            "kb_status",
            new Dictionary<string, object?>(),
            cancellationToken: cancellationToken);
        Assert.False(statusResult.IsError is true);
        var status = JsonSerializer.Deserialize<StatusResponse>(
            ResultText(statusResult),
            JsonOptions.Default);
        Assert.True(status?.Indexing?.IsRunning);
        Assert.Equal(
            "smoke.md",
            status!.Indexing!.Current!.CurrentPath);

        var listResult = await client.CallToolAsync(
            "kb_list_files",
            new Dictionary<string, object?>
            {
                ["request"] = new { }
            },
            cancellationToken: cancellationToken);
        Assert.False(listResult.IsError is true);

        var outlineResult = await client.CallToolAsync(
            "kb_outline",
            new Dictionary<string, object?>
            {
                ["request"] = new { path = "smoke.md" }
            },
            cancellationToken: cancellationToken);
        Assert.False(outlineResult.IsError is true);

        var duplicate = await client.CallToolAsync(
            "kb_reindex",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    scope = "all",
                    force = true
                }
            },
            cancellationToken: cancellationToken);
        Assert.False(duplicate.IsError is true);
        var duplicateStart =
            JsonSerializer.Deserialize<ReindexStartResponse>(
                ResultText(duplicate),
                JsonOptions.Default);
        Assert.NotNull(duplicateStart);
        Assert.False(duplicateStart.Started);
        Assert.Equal(ReindexScope.Changed, duplicateStart.Current.Scope);
        Assert.False(duplicateStart.Current.Force);

        embeddings.Release();

        StatusResponse completed;
        do
        {
            await Task.Delay(20, cancellationToken);
            var result = await client.CallToolAsync(
                "kb_status",
                new Dictionary<string, object?>(),
                cancellationToken: cancellationToken);
            Assert.False(result.IsError is true);
            completed = JsonSerializer.Deserialize<StatusResponse>(
                ResultText(result),
                JsonOptions.Default)
                ?? throw new Xunit.Sdk.XunitException(
                    "kb_status returned invalid JSON.");
        }
        while (completed.Indexing?.IsRunning == true);

        Assert.Equal(
            "succeeded",
            completed.Indexing?.Last?.Outcome);
        Assert.Equal(
            1,
            completed.Indexing?.Last?.Result?.IndexedFiles);
        Assert.DoesNotContain(
            stderr,
            line => line.Contains(
                "threw an unhandled exception",
                StringComparison.Ordinal));
    }

    private static string ResultText(CallToolResult result)
        => string.Join(
            Environment.NewLine,
            result.Content
                .OfType<TextContentBlock>()
                .Select(content => content.Text));

    private static async Task<string> CreateConfigAsync(
        string root,
        int embeddingPort,
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

        var configPath = Path.Combine(root, "slow-reindex.yml");
        var yaml = $"""
            knowledgeBase:
              root: "{ToYamlPath(root)}"
              watchFiles: false
            storage:
              path: "{ToYamlPath(Path.Combine(storageDirectory, "index.db"))}"
            embedding:
              endpoint: "http://127.0.0.1:{embeddingPort}/v1"
              model: "test-model"
              dimensions: 1024
              batchSize: 16
              timeoutSeconds: 30
            """;
        await File.WriteAllTextAsync(
            configPath,
            yaml,
            cancellationToken);
        return configPath;
    }

    private static string ToYamlPath(string path)
        => Path.GetFullPath(path).Replace('\\', '/');

    private sealed class BlockingEmbeddingEndpoint : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource lifetime;
        private readonly Task serveTask;
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingEmbeddingEndpoint(CancellationToken cancellationToken)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            serveTask = ServeAsync(lifetime.Token);
        }

        public int Port { get; }

        public TaskCompletionSource RequestReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => release.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            release.TrySetResult();
            lifetime.Cancel();
            listener.Stop();
            try
            {
                await serveTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException) when (lifetime.IsCancellationRequested)
            {
            }
            lifetime.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            using var client =
                await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                false,
                1024,
                leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(cancellationToken);
            if (requestLine is null)
            {
                throw new IOException("Embedding request ended unexpectedly.");
            }

            var contentLength = 0;
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                const string prefix = "Content-Length:";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(
                        line[prefix.Length..].Trim(),
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            var requestBody = "";
            if (contentLength > 0)
            {
                var body = new char[contentLength];
                var offset = 0;
                while (offset < body.Length)
                {
                    var read = await reader.ReadAsync(
                        body.AsMemory(offset, body.Length - offset),
                        cancellationToken);
                    if (read == 0)
                    {
                        throw new IOException(
                            "Embedding request body ended unexpectedly.");
                    }
                    offset += read;
                }

                requestBody = new string(body);
            }

            RequestReceived.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);

            using var requestJson = JsonDocument.Parse(requestBody);
            var inputCount = requestJson.RootElement
                .GetProperty("input")
                .GetArrayLength();
            var vector = string.Join(
                ",",
                Enumerable.Repeat("0", 1024));
            var data = string.Join(
                ",",
                Enumerable.Range(0, inputCount)
                    .Select(index =>
                        $"{{\"index\":{index},\"embedding\":[{vector}]}}"));
            var payload = $"{{\"data\":[{data}]}}";
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {payloadBytes.Length}\r\n" +
                "Connection: close\r\n\r\n");

            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(payloadBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }
}
