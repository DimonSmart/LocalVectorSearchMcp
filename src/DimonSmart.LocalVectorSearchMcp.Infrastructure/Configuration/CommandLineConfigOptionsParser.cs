using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;

internal static class CommandLineConfigOptionsParser
{
    private static readonly HashSet<string> MaintenanceOptions = new(StringComparer.Ordinal)
    {
        "--reindex",
        "--status",
        "--force"
    };

    public static CommandLineConfigOptions Parse(string[] args)
    {
        string? configPath = null;
        string? root = null;
        string? storagePath = null;
        string? embeddingEndpoint = null;
        string? embeddingModel = null;
        var include = new List<string>();
        var exclude = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--config":
                    configPath = ReadValue(args, ref i, arg);
                    break;
                case "--root":
                    root = ReadValue(args, ref i, arg);
                    break;
                case "--storage-path":
                    storagePath = ReadValue(args, ref i, arg);
                    break;
                case "--embedding-endpoint":
                    embeddingEndpoint = ReadValue(args, ref i, arg);
                    break;
                case "--embedding-model":
                    embeddingModel = ReadValue(args, ref i, arg);
                    break;
                case "--include":
                    include.Add(ReadValue(args, ref i, arg));
                    break;
                case "--exclude":
                    exclude.Add(ReadValue(args, ref i, arg));
                    break;
                default:
                    if (MaintenanceOptions.Contains(arg))
                    {
                        break;
                    }

                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ConfigurationException($"Unknown option: {arg}");
                    }

                    throw new ConfigurationException($"Unexpected argument: {arg}");
            }
        }

        return new CommandLineConfigOptions
        {
            ConfigPath = configPath,
            Root = root,
            StoragePath = storagePath,
            EmbeddingEndpoint = embeddingEndpoint,
            EmbeddingModel = embeddingModel,
            Include = include,
            Exclude = exclude
        };
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ConfigurationException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }
}
