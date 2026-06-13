using AICodingServices.Core;
using AICodingServices.Logging;
using AICodingServices.McpHub;
using CodexUI.Models;

namespace CodexUI.Services;

public sealed class McpServerProcessService : IHostedService, IMcpServerProcessService, IDisposable
{
    private readonly object syncRoot = new();
    private readonly ICodexUiMonitorSettingsProvider settingsProvider;
    private readonly IMcpTelemetrySink telemetrySink;
    private McpProxyHubService? hub;
    private DateTimeOffset? startedAtUtc;
    private string detail = "The AICodingServices MCP hub has not started yet.";
    private string pipeName = "Not started";

    public McpServerProcessService(
        ICodexUiMonitorSettingsProvider settingsProvider,
        IMcpTelemetrySink telemetrySink)
    {
        this.settingsProvider = settingsProvider;
        this.telemetrySink = telemetrySink;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (syncRoot)
        {
            if (hub is not null)
            {
                return Task.CompletedTask;
            }

            MonitorSettings settings = settingsProvider.GetSettings();
            string settingsPath = settingsProvider.GetSettingsPath();
            JsonLinesMonitorLogger logger = new(MonitorLogPaths.GetDefaultLogPath(settings));
            hub = new McpProxyHubService(settings, logger, settingsPath, telemetrySink);
            pipeName = hub.PipeName;
            startedAtUtc = DateTimeOffset.UtcNow;
            detail = $"CodexUI owns the AICodingServices MCP hub. Bridge clients should connect with server '{McpProxyHubService.ServerName}'.";
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        McpProxyHubService? hubToStop;
        lock (syncRoot)
        {
            hubToStop = hub;
            hub = null;
            pipeName = "Stopped";
        }

        if (hubToStop is null)
        {
            return;
        }

        hubToStop.Dispose();
        await Task.CompletedTask;
    }

    public McpServerViewModel GetStatus()
    {
        lock (syncRoot)
        {
            if (hub is not null)
            {
                return new McpServerViewModel(
                    "Running",
                    "state-running",
                    pipeName,
                    "named pipe hub + stdio bridge",
                    startedAtUtc?.ToLocalTime().ToString("g") ?? "Unknown",
                    detail);
            }

            return McpServerViewModel.NotConnected with
            {
                Detail = detail
            };
        }
    }

    public void Dispose()
    {
        McpProxyHubService? hubToDispose;
        lock (syncRoot)
        {
            hubToDispose = hub;
            hub = null;
        }

        if (hubToDispose is null)
        {
            return;
        }

        hubToDispose.Dispose();
    }
}
