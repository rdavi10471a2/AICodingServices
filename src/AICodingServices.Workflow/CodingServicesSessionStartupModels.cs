namespace AICodingServices.Workflow;

public enum CodingServicesTransportKind
{
    None,
    NativeMounted,
    DirectBridge
}

public enum CodingServicesProbeState
{
    Unknown,
    Healthy,
    Missing,
    Failed,
    Unsupported
}

public enum CodingServicesNativeMountState
{
    Unknown,
    Visible,
    Missing,
    Unsupported
}

public sealed record CodingServicesSessionStartupRequest(
    string RepositoryRoot,
    string? SettingsPath = null,
    string SiteUrl = "http://localhost:5000/",
    bool StopExistingProcesses = true,
    bool StartCodexUi = true,
    bool AttemptThreadReset = true);

public sealed record CodingServicesProcessDescriptor(
    int ProcessId,
    string ProcessName,
    string Detail);

public sealed record CodingServicesStartupPaths(
    string RepositoryRoot,
    string SettingsPath,
    string CodexUiExePath,
    string CodexUiDllPath,
    string DirectBridgeDllPath,
    string ServerName,
    string SiteUrl)
{
    public string DirectBridgeCommand =>
        $"dotnet {DirectBridgeDllPath} --repo-root {RepositoryRoot} --config {SettingsPath}";

    public static CodingServicesStartupPaths Create(string repositoryRoot, string? settingsPath, string siteUrl)
    {
        string resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        string resolvedSettingsPath = Path.GetFullPath(
            settingsPath ?? Path.Combine(resolvedRepositoryRoot, "config", "appsettings.json"));

        return new CodingServicesStartupPaths(
            resolvedRepositoryRoot,
            resolvedSettingsPath,
            Path.Combine(resolvedRepositoryRoot, "src", "CodexUI", "bin", "Debug", "net10.0", "CodexUI.exe"),
            Path.Combine(resolvedRepositoryRoot, "src", "CodexUI", "bin", "Debug", "net10.0", "CodexUI.dll"),
            Path.Combine(resolvedRepositoryRoot, "src", "AICodingServices.McpStdioBridge", "bin", "Debug", "net10.0", "AICodingServices.McpStdioBridge.dll"),
            "aicodingservices",
            siteUrl);
    }
}

public sealed record CodingServicesProbeResult(
    CodingServicesProbeState State,
    string Detail,
    long DurationMilliseconds = 0);

public sealed record CodingServicesDirectBridgeProbeResult(
    CodingServicesProbeState State,
    string Detail,
    string? MonitorSummary,
    long DurationMilliseconds = 0);

public sealed record CodingServicesNativeMountResult(
    CodingServicesNativeMountState State,
    string Detail);

public sealed record CodingServicesHostCommandResult(
    bool Supported,
    bool Succeeded,
    string Detail)
{
    public static CodingServicesHostCommandResult Unsupported(string detail)
    {
        return new CodingServicesHostCommandResult(false, false, detail);
    }

    public static CodingServicesHostCommandResult Success(string detail)
    {
        return new CodingServicesHostCommandResult(true, true, detail);
    }

    public static CodingServicesHostCommandResult Failure(string detail)
    {
        return new CodingServicesHostCommandResult(true, false, detail);
    }
}

public sealed record CodingServicesStartupStep(
    string Name,
    string Detail);

public sealed record CodingServicesSessionStartupReport(
    string SiteUrl,
    string ServerName,
    string SettingsPath,
    string DirectBridgeCommand,
    IReadOnlyList<CodingServicesProcessDescriptor> StoppedProcesses,
    int? StartedProcessId,
    CodingServicesProbeResult SiteProbe,
    CodingServicesNativeMountResult NativeMount,
    CodingServicesHostCommandResult NativeActivation,
    CodingServicesHostCommandResult ThreadReset,
    CodingServicesDirectBridgeProbeResult DirectBridgeProbe,
    IReadOnlyList<CodingServicesStartupStep> Steps,
    CodingServicesTransportKind ActiveTransport)
{
    public string ToUserMessage()
    {
        string transportText = ActiveTransport switch
        {
            CodingServicesTransportKind.NativeMounted => "native mounted MCP",
            CodingServicesTransportKind.DirectBridge => "direct bridge fallback",
            _ => "no active transport"
        };

        List<string> lines =
        [
            $"Initialize Coding Services complete.",
            $"Site: {SiteProbe.Detail}",
            $"Native MCP: {NativeMount.Detail}",
            $"Thread reset: {ThreadReset.Detail}",
            $"Direct bridge: {DirectBridgeProbe.Detail}",
            $"Active transport: {transportText}."
        ];

        foreach (CodingServicesStartupStep step in Steps)
        {
            lines.Add($"Step {step.Name}: {step.Detail}");
        }

        if (StoppedProcesses.Count > 0)
        {
            lines.Add($"Restarted CodexUI after stopping PID(s): {string.Join(", ", StoppedProcesses.Select(process => process.ProcessId))}.");
        }
        else if (StartedProcessId.HasValue)
        {
            lines.Add($"Started CodexUI with PID {StartedProcessId.Value}.");
        }

        if (ActiveTransport == CodingServicesTransportKind.DirectBridge)
        {
            lines.Add($"Fallback command: {DirectBridgeCommand}");
        }

        if (ActiveTransport == CodingServicesTransportKind.None)
        {
            lines.Add("Host-side mounted-session control is not fully automated here, so a thread reset or host reconnect may still be required.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
