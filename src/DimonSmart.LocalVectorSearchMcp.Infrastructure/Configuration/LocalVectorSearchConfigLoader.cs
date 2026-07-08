using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Yaml;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;

public static class LocalVectorSearchConfigLoader
{
    public static LocalVectorSearchMcpConfig Load(string[] args)
    {
        var path = ResolveConfigPath(args);
        if (!File.Exists(path))
        {
            throw new ConfigurationException($"Configuration file was not found: {path}");
        }

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new WireEnumYamlConverter())
            .IgnoreUnmatchedProperties()
            .Build();
        LocalVectorSearchMcpConfig config;
        try
        {
            config = deserializer.Deserialize<LocalVectorSearchMcpConfig>(yaml) ?? new LocalVectorSearchMcpConfig();
        }
        catch (Exception ex)
        {
            throw new ConfigurationException($"Configuration file is invalid: {path}. {ex.Message}");
        }
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        config = ResolvePaths(config, baseDirectory);
        ConfigValidator.Validate(config);
        return config;
    }

    private static string ResolveConfigPath(string[] args)
    {
        var index = Array.IndexOf(args, "--config");
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }

        var fromEnv = Environment.GetEnvironmentVariable("LOCAL_VECTOR_SEARCH_MCP_CONFIG");
        return string.IsNullOrWhiteSpace(fromEnv) ? "local-vector-search-mcp.yml" : fromEnv;
    }

    private static LocalVectorSearchMcpConfig ResolvePaths(LocalVectorSearchMcpConfig config, string baseDirectory)
        => config with
        {
            Storage = config.Storage with { Path = ToAbsolute(config.Storage.Path, baseDirectory) },
            KnowledgeBases = config.KnowledgeBases.Select(kb => kb with { Root = ToAbsolute(kb.Root, baseDirectory) }).ToList()
        };

    private static string ToAbsolute(string path, string baseDirectory)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
}
