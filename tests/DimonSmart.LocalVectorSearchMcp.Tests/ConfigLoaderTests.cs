using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
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
    [InlineData("--search-mode")]
    [InlineData("--embedding-provider")]
    [InlineData("--project-root")]
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

    [Fact]
    public void Explicit_project_root_overrides_claude_and_current_directory()
    {
        using var cwd = new TemporaryDirectory();
        using var claude = new TemporaryDirectory();
        using var project = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(project.Path, ".idd", "intent"));

        var config = LocalVectorSearchConfigLoader.Load(
            ["--project-root", project.Path, "--root", ".idd/intent", "--embedding-provider", "none", "--search-mode", "lexical"],
            cwd.Path,
            claude.Path);

        Assert.Equal(Path.Combine(project.Path, ".idd", "intent"), config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, ".local-vector-search-mcp", "index.db"), config.Storage.Path);
        Assert.Equal("none", config.Embedding.Provider);
        Assert.Equal(SearchMode.Lexical, config.Search.DefaultMode);
    }

    [Fact]
    public void Relative_project_root_is_rejected()
    {
        using var cwd = new TemporaryDirectory();
        var exception = Assert.Throws<ConfigurationException>(() =>
            LocalVectorSearchConfigLoader.Load(["--project-root", "./relative"], cwd.Path, cwd.Path));
        Assert.Contains("--project-root must be an absolute", exception.Message);
    }

    [Fact]
    public void Missing_project_root_is_rejected()
    {
        using var cwd = new TemporaryDirectory();
        var exception = Assert.Throws<ConfigurationException>(() =>
            LocalVectorSearchConfigLoader.Load(["--project-root", Path.Combine(cwd.Path, "missing")], cwd.Path, cwd.Path));
        Assert.Contains("--project-root directory does not exist", exception.Message);
    }

    [Fact]
    public void Explicit_project_root_resolves_relative_config_path()
    {
        using var cwd = new TemporaryDirectory();
        using var project = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(project.Path, "settings.yml"), "embedding:\n  provider: none\nsearch:\n  defaultMode: lexical\n");
        var config = LocalVectorSearchConfigLoader.Load(
            ["--project-root", project.Path, "--config", "settings.yml"],
            cwd.Path,
            cwd.Path);
        Assert.Equal("none", config.Embedding.Provider);
        Assert.Equal(project.Path, config.KnowledgeBase.Root);
    }

    [Fact]
    public void CLI_embedding_search_and_watcher_override_yaml()
    {
        using var project = new TemporaryDirectory();
        var yaml = Path.Combine(project.Path, "settings.yml");
        File.WriteAllText(yaml, "embedding:\n  provider: openai-compatible\nsearch:\n  defaultMode: hybrid\nknowledgeBase:\n  watchFiles: true\n");
        var config = LocalVectorSearchConfigLoader.Load(
            ["--config", yaml, "--embedding-provider", "none", "--search-mode", "lexical", "--no-watch-files"],
            project.Path,
            project.Path);
        Assert.Equal("none", config.Embedding.Provider);
        Assert.Equal(SearchMode.Lexical, config.Search.DefaultMode);
        Assert.False(config.KnowledgeBase.WatchFiles);
    }


    [Fact]
    public void Config_relative_paths_use_config_directory_with_different_cwd_and_project_root()
    {
        using var cwd = new TemporaryDirectory();
        using var project = new TemporaryDirectory();
        using var otherProject = new TemporaryDirectory();
        var configDirectory = Path.Combine(project.Path, "settings");
        Directory.CreateDirectory(configDirectory);
        var intentDirectory = Path.Combine(project.Path, ".idd", "intent");
        Directory.CreateDirectory(intentDirectory);
        var yaml = Path.Combine(configDirectory, "search.yml");
        File.WriteAllText(yaml, """
            paths:
              relativeTo: config
            storage:
              path: ../.local-vector-search-mcp/index.db
            knowledgeBase:
              root: ../.idd/intent
            """);

        var config = LocalVectorSearchConfigLoader.Load(
            ["--config", yaml], cwd.Path, otherProject.Path);

        Assert.Equal(intentDirectory, config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, ".local-vector-search-mcp", "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Project_relative_paths_remain_default_even_when_config_is_nested()
    {
        using var project = new TemporaryDirectory();
        var configDirectory = Path.Combine(project.Path, "settings");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(project.Path, "docs"));
        File.WriteAllText(Path.Combine(configDirectory, "search.yml"), """
            storage:
              path: .cache/index.db
            knowledgeBase:
              root: docs
            """);

        var config = LocalVectorSearchConfigLoader.Load(
            ["--project-root", project.Path, "--config", "settings/search.yml"],
            project.Path, "");

        Assert.Equal(Path.Combine(project.Path, "docs"), config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, ".cache", "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Config_relative_mode_keeps_cli_paths_project_relative()
    {
        using var cwd = new TemporaryDirectory();
        using var project = new TemporaryDirectory();
        var configDirectory = Path.Combine(project.Path, "settings");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(project.Path, "cli-docs"));
        File.WriteAllText(Path.Combine(configDirectory, "search.yml"), """
            paths:
              relativeTo: config
            storage:
              path: yaml-cache/index.db
            knowledgeBase:
              root: .
            """);

        var config = LocalVectorSearchConfigLoader.Load(
            ["--project-root", project.Path, "--config", "settings/search.yml",
             "--root", "cli-docs", "--storage-path", "cli-cache/index.db"],
            cwd.Path, "");

        Assert.Equal(Path.Combine(project.Path, "cli-docs"), config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project.Path, "cli-cache", "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Config_relative_mode_keeps_explicit_absolute_yaml_paths()
    {
        using var project = new TemporaryDirectory();
        using var elsewhere = new TemporaryDirectory();
        var yaml = Path.Combine(project.Path, "search.yml");
        File.WriteAllText(yaml, $"""
            paths:
              relativeTo: config
            storage:
              path: {Path.Combine(elsewhere.Path, "index.db").Replace('\\', '/')}
            knowledgeBase:
              root: {elsewhere.Path.Replace('\\', '/')}
            """);

        var config = LocalVectorSearchConfigLoader.Load(
            ["--config", yaml], project.Path, "");

        Assert.Equal(elsewhere.Path, config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(elsewhere.Path, "index.db"), config.Storage.Path);
    }

    [Fact]
    public void Config_relative_mode_without_explicit_knowledge_base_root_uses_project_root()
    {
        using var project = new TemporaryDirectory();
        var configDirectory = Path.Combine(project.Path, "settings");
        Directory.CreateDirectory(configDirectory);
        var yaml = Path.Combine(configDirectory, "search.yml");
        File.WriteAllText(yaml, """
            paths:
              relativeTo: config
            embedding:
              provider: none
            """);

        var config = LocalVectorSearchConfigLoader.Load(
            ["--config", yaml], project.Path, "");

        Assert.Equal(project.Path, config.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(configDirectory, ".local-vector-search-mcp", "index.db"), config.Storage.Path);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public void Invalid_paths_relative_to_is_rejected(string value)
    {
        using var project = new TemporaryDirectory();
        var yaml = Path.Combine(project.Path, "search.yml");
        File.WriteAllText(yaml, $"paths:\n  relativeTo: '{value}'\n");

        var exception = Assert.Throws<ConfigurationException>(() =>
            LocalVectorSearchConfigLoader.Load(["--config", yaml], project.Path, ""));

        Assert.Contains("paths.relativeTo must be project or config", exception.Message);
    }

    [Fact]
    public void Idd_config_relative_layout_resolves_from_arbitrary_cwd_with_spaces_and_unicode()
    {
        using var temp = new TemporaryDirectory();
        using var cwd = new TemporaryDirectory();
        var project = Path.Combine(temp.Path, "Проект с пробелами");
        var intent = Path.Combine(project, ".idd", "intent");
        Directory.CreateDirectory(intent);
        var configPath = Path.Combine(project, ".idd", "local-vector-search-mcp.yml");
        File.WriteAllText(configPath, """
            paths:
              relativeTo: config
            storage:
              path: ../.local-vector-search-mcp/index.db
            embedding:
              provider: none
            search:
              defaultMode: lexical
            knowledgeBase:
              root: intent
              allowWrites: false
              watchFiles: true
              include:
                - "IDD-*.md"
              exclude:
                - "**/*.cosmic.md"
            """);

        var loaded = LocalVectorSearchConfigLoader.Load(
            ["--project-root", project, "--config", ".idd/local-vector-search-mcp.yml"],
            cwd.Path,
            "");

        Assert.Equal(intent, loaded.KnowledgeBase.Root);
        Assert.Equal(Path.Combine(project, ".local-vector-search-mcp", "index.db"), loaded.Storage.Path);
        Assert.False(loaded.KnowledgeBase.AllowWrites);
        Assert.True(loaded.KnowledgeBase.WatchFiles);
        Assert.Equal(["IDD-*.md"], loaded.KnowledgeBase.Include);
        Assert.Equal(["**/*.cosmic.md"], loaded.KnowledgeBase.Exclude);
        Assert.Equal("none", loaded.Embedding.Provider);
        Assert.Equal(SearchMode.Lexical, loaded.Search.DefaultMode);
    }

    [Fact]
    public void Invalid_search_mode_is_rejected()
    {
        using var project = new TemporaryDirectory();
        Assert.Throws<ConfigurationException>(() => LocalVectorSearchConfigLoader.Load(
            ["--search-mode", "incorrect"], project.Path, project.Path));
    }

}
