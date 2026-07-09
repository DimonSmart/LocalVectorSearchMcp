using System.Net;
using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Embeddings;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class OpenAiCompatibleEmbeddingProviderTests
{
    [Fact]
    public async Task EmbedBatchAsync_EmptyInput_ReturnsEmptyWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler("""{"data":[]}""");
        var result = await CreateProvider(handler).EmbedBatchAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task EmbedBatchAsync_NonSuccess_ThrowsEmbeddingProviderException()
    {
        var handler = new StubHttpMessageHandler("failure", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => CreateProvider(handler).EmbedBatchAsync(["text"], TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{"data":[{"embedding":[0.1]}]}""")]
    [InlineData("""{"data":[{"index":0}]}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"index":1,"embedding":[0.1]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[0.1]},{"index":0,"embedding":[0.2]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[]}]}""")]
    public async Task EmbedBatchAsync_InvalidResponse_ThrowsEmbeddingProviderException(string response)
    {
        var handler = new StubHttpMessageHandler(response);

        await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => CreateProvider(handler).EmbedBatchAsync(["text"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsVectorsOrderedByIndex()
    {
        var handler = new StubHttpMessageHandler(
            """{"data":[{"index":1,"embedding":[0.3,0.4]},{"index":0,"embedding":[0.1,0.2]}]}""");

        var vectors = await CreateProvider(handler)
            .EmbedBatchAsync(["first", "second"], TestContext.Current.CancellationToken);

        Assert.Equal([0.1f, 0.2f], vectors[0].Values);
        Assert.Equal([0.3f, 0.4f], vectors[1].Values);
    }

    private static OpenAiCompatibleEmbeddingProvider CreateProvider(HttpMessageHandler handler)
        => new(new HttpClient(handler), new LocalVectorSearchMcpConfig());

    private sealed class StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
