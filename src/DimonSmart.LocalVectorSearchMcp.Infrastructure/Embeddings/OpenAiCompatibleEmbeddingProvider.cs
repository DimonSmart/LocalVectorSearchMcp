using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Embeddings;

public sealed class OpenAiCompatibleEmbeddingProvider(HttpClient httpClient, LocalVectorSearchMcpConfig config) : IEmbeddingProvider
{
    public async Task<IReadOnlyList<EmbeddingVector>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0) return [];

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(config.Embedding.Endpoint.TrimEnd('/') + "/"), "embeddings"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Embedding.ApiKey);
        request.Content = JsonContent.Create(new { model = config.Embedding.Model, input = texts });

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new EmbeddingProviderException($"Embedding endpoint returned {(int)response.StatusCode}: {config.Embedding.Endpoint}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ParseEmbeddingResponse(json.RootElement, texts.Count);
        }
        catch (EmbeddingProviderException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new EmbeddingProviderException("Embedding endpoint returned invalid JSON.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new EmbeddingProviderException("Embedding endpoint request failed.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmbeddingProviderException("Embedding endpoint request timed out.", exception);
        }
        catch (IOException exception)
        {
            throw new EmbeddingProviderException("Embedding endpoint response could not be read.", exception);
        }
    }

    private static IReadOnlyList<EmbeddingVector> ParseEmbeddingResponse(JsonElement root, int expectedCount)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new EmbeddingProviderException("Embedding response is missing 'data' array.");
        }

        var vectors = new EmbeddingVector?[expectedCount];
        var returnedCount = 0;
        foreach (var item in data.EnumerateArray())
        {
            returnedCount++;
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("index", out var indexElement)
                || !indexElement.TryGetInt32(out var index))
            {
                throw new EmbeddingProviderException("Embedding response item is missing valid 'index'.");
            }

            if (index < 0 || index >= expectedCount)
            {
                throw new EmbeddingProviderException($"Embedding response index {index} is out of range for batch size {expectedCount}.");
            }

            if (vectors[index] is not null)
            {
                throw new EmbeddingProviderException($"Embedding response contains duplicate index {index}.");
            }

            if (!item.TryGetProperty("embedding", out var embedding)
                || embedding.ValueKind != JsonValueKind.Array)
            {
                throw new EmbeddingProviderException($"Embedding response item {index} is missing 'embedding' array.");
            }

            var values = new List<float>();
            foreach (var value in embedding.EnumerateArray())
            {
                if (!value.TryGetSingle(out var parsedValue))
                {
                    throw new EmbeddingProviderException($"Embedding response item {index} contains a non-numeric embedding value.");
                }

                values.Add(parsedValue);
            }

            if (values.Count == 0)
            {
                throw new EmbeddingProviderException($"Embedding response item {index} contains an empty embedding.");
            }

            vectors[index] = new EmbeddingVector(values.ToArray());
        }

        if (returnedCount != expectedCount || vectors.Any(vector => vector is null))
        {
            throw new EmbeddingProviderException($"Embedding response returned {returnedCount} embeddings for {expectedCount} input texts.");
        }

        return vectors.Select(vector => vector!).ToArray();
    }
}
