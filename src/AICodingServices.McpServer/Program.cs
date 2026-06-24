using AICodingServices.Core;
using AICodingServices.Data;
using AICodingServices.Indexing;
using AICodingServices.Logging;
using AICodingServices.Runtime;
using AICodingServices.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICodingServices.McpServer;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        MonitorSettings settings = LoadSettings(args);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<IMonitorLogger>(_ => new MonitorLogPipeClientLogger(
            MonitorLogPipeNames.GetDefaultPipeName(settings),
            new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings))));
        builder.Services.AddSingleton(SolutionIndexQueryService.Create(settings));
        builder.Services.AddSingleton(new WorkflowEditService(settings));
        builder.Services.AddSingleton(new RoslynEditService(settings));
        builder.Services.AddSingleton(new WorkflowEditPaths(settings));
        builder.Services.AddSingleton<AICodingServicesMcpRuntimeState>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<AICodingServicesTools>();

        await builder.Build().RunAsync();
    }

    private static MonitorSettings LoadSettings(string[] args)
    {
        string repositoryRoot = GetOption(args, "--repo-root") ?? Directory.GetCurrentDirectory();
        string? settingsPath = GetOption(args, "--config");
        return MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}