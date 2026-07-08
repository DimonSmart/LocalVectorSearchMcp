using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure;

public sealed class OpenAiCompatibleEmbeddingProvider(HttpClient httpClient, LocalVectorSearchMcpConfig config) : IEmbeddingProvider
{
    public async Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0) return [];
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(config.Embedding.Endpoint.TrimEnd('/') + "/"), "embeddings"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Embedding.ApiKey);
        request.Content = JsonContent.Create(new { model = config.Embedding.Model, input = texts });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new EmbeddingProviderException($"Embedding endpoint returned {(int)response.StatusCode}: {config.Embedding.Endpoint}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("data").EnumerateArray()
            .OrderBy(e => e.GetProperty("index").GetInt32())
            .Select(e => new EmbeddingVector(e.GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray()))
            .ToList();
    }
}
