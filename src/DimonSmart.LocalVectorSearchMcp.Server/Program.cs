using DimonSmart.LocalVectorSearchMcp.Infrastructure.Configuration;
using DimonSmart.LocalVectorSearchMcp.Server;
using DimonSmart.LocalVectorSearchMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var maintenanceOptions = MaintenanceCommandOptions.Parse(args);

try
{
    var config = LocalVectorSearchConfigLoader.Load(args);
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddLocalVectorSearchMcp(config);

    if (maintenanceOptions.IsMaintenanceCommand)
    {
        using var host = builder.Build();
        await MaintenanceCommandRunner.RunAsync(
            host.Services,
            maintenanceOptions,
            CancellationToken.None);
        return;
    }

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(KnowledgeMcpTools).Assembly, JsonOptions.Default);

    await builder.Build().RunAsync();
}
catch (Exception ex) when (KnownCliExceptionFilter.IsKnown(ex))
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
