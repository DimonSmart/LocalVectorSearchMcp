using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;
using DimonSmart.LocalVectorSearchMcp.Tests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_without_config_uses_defaults_and_project_root()
    {
        using var temp = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load([], temp.Path, "");

        Assert.Equal(temp.Path, config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(temp.Path, ".local-vector-search-mcp", "index.db"), config.Storage.Path);
        Assert.Equal(["**/*.md"], config.KnowledgeBase.Include);
        Assert.Empty(config.KnowledgeBase.Exclude);
        Assert.Equal("http://localhost:11434/v1", config.Embedding.Endpoint);
        Assert.Equal("bge-m3:latest", config.Embedding.Model);
    }

    [Fact]
    public void Load_without_config_uses_claude_project_dir_when_available()
    {
        using var current = new TemporaryDirectory();
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load([], current.Path, project.Path);

        Assert.Equal(project.Path, config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, ".local-vector-search-mcp", "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Load_without_config_uses_current_directory_when_claude_project_dir_missing()
    {
        using var current = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load([], current.Path, "");

        Assert.Equal(current.Path, config.KnowledgeBase.Root);
    }

    [Fact]
    public void Load_with_root_cli_argument_overrides_default_root()
    {
        using var project = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(project.Path, "docs"));

        var config = LocalVectorSearchConfigLoader.Load(["--root", "docs"], project.Path, project.Path);

        Assert.Equal(Path.Combine(project.Path, "docs"), config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, ".local-vector-search-mcp", "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Load_with_storage_path_cli_argument_overrides_default_storage()
    {
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load(["--storage-path", ".cache/index.db"], project.Path, project.Path);

        Assert.Equal(Path.Combine(project.Path, ".cache", "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Load_with_embedding_cli_arguments_overrides_default_embedding()
    {
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load(
            ["--embedding-endpoint", "http://localhost:11434/v1", "--embedding-model", "bge-m3:latest"],
            project.Path,
            project.Path);

        Assert.Equal("http://localhost:11434/v1", config.Embedding.Endpoint);
        Assert.Equal("bge-m3:latest", config.Embedding.Model);
    }

    [Fact]
    public void Default_include_is_markdown_and_default_exclude_is_empty()
    {
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load([], project.Path, project.Path);

        Assert.Equal(["**/*.md"], config.KnowledgeBase.Include);
        Assert.Empty(config.KnowledgeBase.Exclude);
    }

    [Fact]
    public void Load_with_include_exclude_cli_arguments_replaces_patterns()
    {
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load(
            [
                "--include", "docs/**/*.md",
                "--include", "specs/**/*.md",
                "--exclude", "**/node_modules/**",
                "--exclude", "**/.git/**"
            ],
            project.Path,
            project.Path);

        Assert.Equal(["docs/**/*.md", "specs/**/*.md"], config.KnowledgeBase.Include);
        Assert.Equal(["**/node_modules/**", "**/.git/**"], config.KnowledgeBase.Exclude);
    }

    [Fact]
    public void Load_with_explicit_missing_config_throws_configuration_exception()
    {
        using var project = new TemporaryDirectory();
        var missing = Path.Combine(project.Path, "missing.yml");

        var exception = Assert.Throws<ConfigurationException>(
            () => LocalVectorSearchConfigLoader.Load(["--config", "missing.yml"], project.Path, project.Path));

        Assert.Contains($"Configuration file was not found: {missing}", exception.Message);
    }

    [Fact]
    public void Load_without_config_does_not_require_local_vector_search_mcp_yml()
    {
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load([], project.Path, project.Path);

        Assert.Equal(project.Path, config.KnowledgeBase.Root);
    }

    [Fact]
    public void Load_with_config_file_loads_yaml_values()
    {
        using var project = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(project.Path, "docs"));
        var configPath = Path.Combine(project.Path, "config.yml");
        File.WriteAllText(configPath, """
            storage:
              path: .cache/index.db
            embedding:
              model: yaml-model
            knowledgeBase:
              root: docs
              include:
                - docs/**/*.md
              exclude:
                - docs/private/**
            """);

        var config = LocalVectorSearchConfigLoader.Load(["--config", configPath], project.Path, project.Path);

        Assert.Equal(Path.Combine(project.Path, "docs"), config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, ".cache", "index.db"), config.Storage.Path);
        Assert.Equal("yaml-model", config.Embedding.Model);
        Assert.Equal(["docs/**/*.md"], config.KnowledgeBase.Include);
        Assert.Equal(["docs/private/**"], config.KnowledgeBase.Exclude);
    }

    [Fact]
    public void Load_with_config_and_cli_overrides_applies_cli_last()
    {
        using var project = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(project.Path, "yaml-docs"));
        Directory.CreateDirectory(Path.Combine(project.Path, "cli-docs"));
        var configPath = Path.Combine(project.Path, "config.yml");
        File.WriteAllText(configPath, """
            embedding:
              model: yaml-model
            knowledgeBase:
              root: yaml-docs
            """);

        var config = LocalVectorSearchConfigLoader.Load(
            ["--config", configPath, "--root", "cli-docs", "--embedding-model", "cli-model"],
            project.Path,
            project.Path);

        Assert.Equal(Path.Combine(project.Path, "cli-docs"), config.KnowledgeBase.Root);
        Assert.Equal("cli-model", config.Embedding.Model);
    }

    [Theory]
    [InlineData("--root")]
    [InlineData("--storage-path")]
    [InlineData("--embedding-endpoint")]
    [InlineData("--embedding-model")]
    [InlineData("--include")]
    [InlineData("--exclude")]
    public void CommandLineConfigOptionsParser_rejects_missing_values(string option)
    {
        var exception = Assert.Throws<ConfigurationException>(
            () => CommandLineConfigOptionsParser.Parse([option]));

        Assert.Contains($"{option} requires a value.", exception.Message);
    }

    [Fact]
    public void CommandLineConfigOptionsParser_ignores_maintenance_options()
    {
        var options = CommandLineConfigOptionsParser.Parse(["--status", "--reindex", "--force", "--root", "docs"]);

        Assert.Equal("docs", options.Root);
    }

    [Fact]
    public void CommandLineConfigOptionsParser_rejects_unknown_options()
    {
        var exception = Assert.Throws<ConfigurationException>(
            () => CommandLineConfigOptionsParser.Parse(["--abc"]));

        Assert.Contains("Unknown option: --abc", exception.Message);
    }

    [Fact]
    public void Status_command_works_without_config_file()
    {
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load(["--status"], project.Path, project.Path);

        Assert.Equal(project.Path, config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, ".local-vector-search-mcp", "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Reindex_command_can_start_without_config_file()
    {
        using var project = new TemporaryDirectory();

        var config = LocalVectorSearchConfigLoader.Load(["--reindex", "--force"], project.Path, project.Path);

        Assert.Equal(project.Path, config.KnowledgeBase.Root);
    }
}
