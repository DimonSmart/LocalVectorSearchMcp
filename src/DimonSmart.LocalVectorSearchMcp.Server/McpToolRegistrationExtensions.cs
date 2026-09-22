using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalVectorSearchMcp.Server;

internal static class McpToolRegistrationExtensions
{
    private static readonly AIJsonSchemaCreateOptions ParameterlessToolSchemaOptions =
        new()
        {
            TransformOptions = new AIJsonSchemaTransformOptions
            {
                DisallowAdditionalProperties = true
            }
        };

    public static IMcpServerBuilder WithLocalVectorSearchToolsFromAssembly(
        this IMcpServerBuilder builder,
        Assembly toolAssembly,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(toolAssembly);

        foreach (var toolType in toolAssembly.GetTypes()
                     .Where(type =>
                         type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            foreach (var toolMethod in toolType.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.Static |
                         BindingFlags.Instance))
            {
                if (toolMethod.GetCustomAttribute<McpServerToolAttribute>() is null)
                {
                    continue;
                }

                builder.Services.AddSingleton(
                    (Func<IServiceProvider, McpServerTool>)(services =>
                        CreateTool(
                            toolType,
                            toolMethod,
                            services,
                            serializerOptions)));
            }
        }

        return builder;
    }

    private static McpServerTool CreateTool(
        Type toolType,
        MethodInfo toolMethod,
        IServiceProvider services,
        JsonSerializerOptions? serializerOptions)
    {
        var options = new McpServerToolCreateOptions
        {
            Services = services,
            SerializerOptions = serializerOptions
        };

        var tool = CreateTool(
            toolType,
            toolMethod,
            options);

        if (!HasNoUserArguments(tool.ProtocolTool.InputSchema))
        {
            return tool;
        }

        options.SchemaCreateOptions =
            ParameterlessToolSchemaOptions;

        return CreateTool(
            toolType,
            toolMethod,
            options);
    }

    private static McpServerTool CreateTool(
        Type toolType,
        MethodInfo toolMethod,
        McpServerToolCreateOptions options)
        => toolMethod.IsStatic
            ? McpServerTool.Create(
                toolMethod,
                options: options)
            : McpServerTool.Create(
                toolMethod,
                request => CreateTarget(
                    request.Services,
                    toolType),
                options);

    private static bool HasNoUserArguments(
        JsonElement inputSchema)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object
            || !inputSchema.TryGetProperty(
                "type",
                out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() != "object")
        {
            return false;
        }

        return !inputSchema.TryGetProperty(
                   "properties",
                   out var properties)
               || properties.ValueKind == JsonValueKind.Object
               && !properties.EnumerateObject().Any();
    }

    private static object CreateTarget(
        IServiceProvider? services,
        Type toolType)
        => services is not null
            ? ActivatorUtilities.CreateInstance(
                services,
                toolType)
            : Activator.CreateInstance(toolType)
              ?? throw new InvalidOperationException(
                  $"Could not create MCP tool type '{toolType.FullName}'.");
}
