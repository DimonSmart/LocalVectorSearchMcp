using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Yaml;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;

public static class LocalVectorSearchConfigLoader
{
    public static LocalVectorSearchMcpConfig Load(
        string[] args,
        string? currentDirectory = null,
        string? claudeProjectDirectory = null)
    {
        var options = CommandLineConfigOptionsParser.Parse(args);
        var baseCurrentDirectory = Path.GetFullPath(currentDirectory ?? Directory.GetCurrentDirectory());
        var projectRoot = ResolveProjectRoot(baseCurrentDirectory, claudeProjectDirectory ?? Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR"));

        var config = new LocalVectorSearchMcpConfig();
        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            config = LoadYaml(ResolveConfigPath(options.ConfigPath, baseCurrentDirectory));
        }

        config = ApplyCliOverrides(config, options);
        config = ResolvePaths(config, projectRoot);
        ConfigValidator.Validate(config);
        return config;
    }

    private static LocalVectorSearchMcpConfig LoadYaml(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConfigurationException($"Configuration file was not found: {path}");
        }

        var yaml = File.ReadAllText(path);
        if (Regex.IsMatch(yaml, @"(?m)^knowledgeBases\s*:"))
        {
            throw new ConfigurationException("knowledgeBases list is no longer supported. Use singular knowledgeBase instead.");
        }
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new WireEnumYamlConverter())
            .WithTypeConverter(new ReadOnlyStringListYamlConverter())
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

        return NormalizeSections(config);
    }

    private static LocalVectorSearchMcpConfig NormalizeSections(LocalVectorSearchMcpConfig config)
        => config with
        {
            Server = config.Server ?? new(),
            Storage = config.Storage ?? new(),
            Embedding = config.Embedding ?? new(),
            Chunking = config.Chunking ?? new(),
            Search = config.Search ?? new(),
            KnowledgeBase = config.KnowledgeBase ?? new()
        };

    private static string ResolveConfigPath(string path, string currentDirectory)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(currentDirectory, path));

    private static string ResolveProjectRoot(string currentDirectory, string? claudeProjectDirectory)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(claudeProjectDirectory) ? currentDirectory : claudeProjectDirectory);

    private static LocalVectorSearchMcpConfig ApplyCliOverrides(LocalVectorSearchMcpConfig config, CommandLineConfigOptions options)
        => config with
        {
            Storage = config.Storage with
            {
                Path = options.StoragePath ?? config.Storage.Path
            },
            Embedding = config.Embedding with
            {
                Endpoint = options.EmbeddingEndpoint ?? config.Embedding.Endpoint,
                Model = options.EmbeddingModel ?? config.Embedding.Model
            },
            KnowledgeBase = config.KnowledgeBase with
            {
                Root = options.Root ?? config.KnowledgeBase.Root,
                Include = options.Include.Count > 0 ? options.Include : config.KnowledgeBase.Include,
                Exclude = options.Exclude.Count > 0 ? options.Exclude : config.KnowledgeBase.Exclude
            }
        };

    private static LocalVectorSearchMcpConfig ResolvePaths(LocalVectorSearchMcpConfig config, string projectRoot)
        => config with
        {
            Storage = config.Storage with { Path = ToAbsolute(DefaultStoragePath(config.Storage.Path, projectRoot), projectRoot) },
            KnowledgeBase = config.KnowledgeBase with { Root = ToAbsolute(DefaultRoot(config.KnowledgeBase.Root, projectRoot), projectRoot) }
        };

    private static string DefaultRoot(string root, string projectRoot)
        => string.IsNullOrWhiteSpace(root) ? projectRoot : root;

    private static string DefaultStoragePath(string storagePath, string projectRoot)
        => string.IsNullOrWhiteSpace(storagePath)
            ? Path.Combine(projectRoot, ".local-vector-search-mcp", "index.db")
            : storagePath;

    private static string ToAbsolute(string path, string baseDirectory)
        => string.IsNullOrWhiteSpace(path)
            ? path
            : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
}
