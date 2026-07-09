using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;

namespace DimonSmart.LocalVectorSearchMcp.Core.Configuration;

public static class ConfigValidator
{
    public static void Validate(LocalVectorSearchMcpConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Storage.Path)) throw new ConfigurationException("storage.path is required.");
        if (string.IsNullOrWhiteSpace(config.KnowledgeBase.Root)) throw new ConfigurationException("knowledgeBase.root is required.");
        if (!Path.IsPathFullyQualified(config.KnowledgeBase.Root)) throw new ConfigurationException("knowledgeBase.root must be absolute after path resolution.");
        if (!Directory.Exists(config.KnowledgeBase.Root)) throw new ConfigurationException("knowledgeBase.root does not exist.");
        if (config.KnowledgeBase.Include.Count == 0) throw new ConfigurationException("knowledgeBase.include must contain at least one pattern.");
        if (config.KnowledgeBase.Include.Concat(config.KnowledgeBase.Exclude).Any(string.IsNullOrWhiteSpace)) throw new ConfigurationException("knowledgeBase include and exclude patterns must not be blank.");
        if (config.Embedding.Provider != "openai-compatible") throw new ConfigurationException("embedding.provider must be openai-compatible.");
        if (string.IsNullOrWhiteSpace(config.Embedding.ApiKey)) throw new ConfigurationException("embedding.apiKey is required.");
        if (config.Embedding.BatchSize < 1) throw new ConfigurationException("embedding.batchSize must be greater than 0.");
        if (config.Chunking.MaxChunkBytes < 1) throw new ConfigurationException("chunking.maxChunkBytes must be greater than 0.");
        if (config.Chunking.MaxElements < 1) throw new ConfigurationException("chunking.maxElements must be greater than 0.");
        if (!Uri.TryCreate(config.Embedding.Endpoint, UriKind.Absolute, out var endpoint)) throw new ConfigurationException("embedding.endpoint must be an absolute URI.");
        if (!config.Embedding.AllowRemoteEndpoint && !IsLoopbackHost(endpoint)) throw new ConfigurationException("embedding.endpoint must be loopback unless allowRemoteEndpoint is true.");

    }

    private static bool IsLoopbackHost(Uri endpoint)
        => endpoint.IsLoopback || endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || endpoint.Host is "127.0.0.1" or "::1" or "[::1]";
}
