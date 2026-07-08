using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;

namespace DimonSmart.LocalVectorSearchMcp.Core.Configuration;

public static class ConfigValidator
{
    public static void Validate(LocalVectorSearchMcpConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Storage.Path)) throw new ConfigurationException("storage.path is required.");
        if (config.KnowledgeBases.Count == 0) throw new ConfigurationException("At least one knowledge base must be configured.");
        if (config.Embedding.Provider != "openai-compatible") throw new ConfigurationException("embedding.provider must be openai-compatible.");
        if (string.IsNullOrWhiteSpace(config.Embedding.ApiKey)) throw new ConfigurationException("embedding.apiKey is required.");
        if (config.Embedding.BatchSize < 1) throw new ConfigurationException("embedding.batchSize must be greater than 0.");
        if (!Uri.TryCreate(config.Embedding.Endpoint, UriKind.Absolute, out var endpoint)) throw new ConfigurationException("embedding.endpoint must be an absolute URI.");
        if (!config.Embedding.AllowRemoteEndpoint && !IsLoopbackHost(endpoint)) throw new ConfigurationException("embedding.endpoint must be loopback unless allowRemoteEndpoint is true.");

        for (var i = 0; i < config.KnowledgeBases.Count; i++)
        {
            var kb = config.KnowledgeBases[i];
            if (string.IsNullOrWhiteSpace(kb.Name)) throw new ConfigurationException($"knowledgeBases[{i}].name is required.");
            if (string.IsNullOrWhiteSpace(kb.Root)) throw new ConfigurationException($"knowledgeBases[{i}].root is required.");
            if (!Directory.Exists(kb.Root)) throw new ConfigurationException($"knowledgeBases[{i}].root does not exist.");
        }
    }

    private static bool IsLoopbackHost(Uri endpoint)
        => endpoint.IsLoopback || endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || endpoint.Host is "127.0.0.1" or "::1" or "[::1]";
}
