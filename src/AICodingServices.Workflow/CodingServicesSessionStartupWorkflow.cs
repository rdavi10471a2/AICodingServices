using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace AICodingServices.Workflow;

public interface ICodingServicesSessionStartupRuntime
{
    Task<IReadOnlyList<CodingServicesProcessDescriptor>> FindRunningCodexUiProcessesAsync(CodingServicesStartupPaths paths, CancellationToken cancellationToken);
    Task StopProcessAsync(int processId, CancellationToken cancellationToken);
    Task<int?> StartCodexUiAsync(CodingServicesStartupPaths paths, CancellationToken cancellationToken);
    Task<CodingServicesProbeResult> ProbeSiteAsync(string siteUrl, CancellationToken cancellationToken);
    Task<CodingServicesDirectBridgeProbeResult> ProbeDirectBridgeAsync(CodingServicesStartupPaths paths, CancellationToken cancellationToken);
}

public interface ICodingServicesSessionHostController
{
    Task<CodingServicesNativeMountResult> GetNativeMountStatusAsync(string serverName, CancellationToken cancellationToken);
    Task<CodingServicesHostCommandResult> ActivateNativeMountedServerAsync(string serverName, CancellationToken cancellationToken);
    Task<CodingServicesHostCommandResult> ResetThreadAsync(CancellationToken cancellationToken);
}

public sealed class NullCodingServicesSessionHostController : ICodingServicesSessionHostController
{
    public Task<CodingServicesHostCommandResult> ActivateNativeMountedServerAsync(string serverName, CancellationToken cancellationToken)
    {
        return Task.FromResult(CodingServicesHostCommandResult.Unsupported(
            $"Mounted server '{serverName}' is not host-controllable from this runtime."));
    }

    public Task<CodingServicesNativeMountResult> GetNativeMountStatusAsync(string serverName, CancellationToken cancellationToken)
    {
        return Task.FromResult(new CodingServicesNativeMountResult(
            CodingServicesNativeMountState.Unsupported,
            $"Mounted server '{serverName}' is not introspectable from this runtime."));
    }

    public Task<CodingServicesHostCommandResult> ResetThreadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(CodingServicesHostCommandResult.Unsupported(
            "Thread reset is host-owned and not available from this runtime."));
    }
}

public sealed class CodingServicesSessionStartupWorkflow
{
    private readonly ICodingServicesSessionStartupRuntime runtime;
    private readonly ICodingServicesSessionHostController hostController;

    public CodingServicesSessionStartupWorkflow(
        ICodingServicesSessionStartupRuntime runtime,
        ICodingServicesSessionHostController hostController)
    {
        this.runtime = runtime;
        this.hostController = hostController;
    }

    public async Task<CodingServicesSessionStartupReport> InitializeAsync(
        CodingServicesSessionStartupRequest request,
        Func<CodingServicesStartupStep, CancellationToken, Task>? onStepAsync = null,
        CancellationToken cancellationToken = default)
    {
        List<CodingServicesStartupStep> steps = [];
        CodingServicesStartupPaths paths = CodingServicesStartupPaths.Create(
            request.RepositoryRoot,
            request.SettingsPath,
            request.SiteUrl);
        await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep(
            "paths",
            $"Resolved site {paths.SiteUrl}, server {paths.ServerName}, and settings {paths.SettingsPath}."), cancellationToken);

        IReadOnlyList<CodingServicesProcessDescriptor> runningProcesses =
            await runtime.FindRunningCodexUiProcessesAsync(paths, cancellationToken);
        await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep(
            "discover",
            runningProcesses.Count == 0
                ? "No running CodexUI process was discovered."
                : $"Found {runningProcesses.Count} running CodexUI process(es)."), cancellationToken);

        List<CodingServicesProcessDescriptor> stoppedProcesses = [];
        int? startedProcessId = null;

        CodingServicesProbeResult siteProbe = await runtime.ProbeSiteAsync(paths.SiteUrl, cancellationToken);
        await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("site", siteProbe.Detail), cancellationToken);

        if (siteProbe.State == CodingServicesProbeState.Healthy)
        {
            await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep(
                "site-keepalive",
                runningProcesses.Count == 0
                    ? "CodexUI is already reachable, so no restart was needed."
                    : "CodexUI is already reachable, so the running process was left in place."), cancellationToken);
        }
        else
        {
            if (request.StopExistingProcesses)
            {
                foreach (CodingServicesProcessDescriptor process in runningProcesses)
                {
                    await runtime.StopProcessAsync(process.ProcessId, cancellationToken);
                    stoppedProcesses.Add(process);
                }

                await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep(
                    "stop",
                    stoppedProcesses.Count == 0
                        ? "Site probe failed, but no running CodexUI process needed to be stopped."
                        : $"Site probe failed, so {stoppedProcesses.Count} running CodexUI process(es) were stopped."), cancellationToken);
            }

            if (request.StartCodexUi)
            {
                startedProcessId = await runtime.StartCodexUiAsync(paths, cancellationToken);
                await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep(
                    "start",
                    startedProcessId.HasValue
                        ? $"Started CodexUI with PID {startedProcessId.Value} on {paths.SiteUrl}."
                        : "CodexUI did not return a new process id on start."), cancellationToken);
                siteProbe = await runtime.ProbeSiteAsync(paths.SiteUrl, cancellationToken);
                await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("site-recheck", siteProbe.Detail), cancellationToken);
            }
        }

        CodingServicesNativeMountResult nativeMount = await hostController.GetNativeMountStatusAsync(paths.ServerName, cancellationToken);
        await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("native-check", nativeMount.Detail), cancellationToken);
        CodingServicesHostCommandResult nativeActivation = CodingServicesHostCommandResult.Unsupported(
            $"Mounted server '{paths.ServerName}' was not activated.");
        CodingServicesHostCommandResult threadReset = CodingServicesHostCommandResult.Unsupported(
            "Thread reset was not attempted.");
        CodingServicesTransportKind activeTransport = CodingServicesTransportKind.None;

        if (nativeMount.State == CodingServicesNativeMountState.Visible)
        {
            nativeActivation = await hostController.ActivateNativeMountedServerAsync(paths.ServerName, cancellationToken);
            await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("native-activate", nativeActivation.Detail), cancellationToken);
            if (nativeActivation.Succeeded)
            {
                activeTransport = CodingServicesTransportKind.NativeMounted;
            }
        }
        else if (request.AttemptThreadReset)
        {
            threadReset = await hostController.ResetThreadAsync(cancellationToken);
            await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("thread-reset", threadReset.Detail), cancellationToken);
            if (threadReset.Supported && threadReset.Succeeded)
            {
                nativeMount = await hostController.GetNativeMountStatusAsync(paths.ServerName, cancellationToken);
                await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("native-recheck", nativeMount.Detail), cancellationToken);
                if (nativeMount.State == CodingServicesNativeMountState.Visible)
                {
                    nativeActivation = await hostController.ActivateNativeMountedServerAsync(paths.ServerName, cancellationToken);
                    await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("native-activate", nativeActivation.Detail), cancellationToken);
                    if (nativeActivation.Succeeded)
                    {
                        activeTransport = CodingServicesTransportKind.NativeMounted;
                    }
                }
            }
        }

        CodingServicesDirectBridgeProbeResult directBridgeProbe;
        if (activeTransport == CodingServicesTransportKind.NativeMounted)
        {
            directBridgeProbe = new CodingServicesDirectBridgeProbeResult(
                CodingServicesProbeState.Healthy,
                "Direct bridge fallback was not needed because native mounted MCP is active.",
                null);
            await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("bridge-skip", directBridgeProbe.Detail), cancellationToken);
        }
        else
        {
            directBridgeProbe = await runtime.ProbeDirectBridgeAsync(paths, cancellationToken);
            await AddStepAsync(steps, onStepAsync, new CodingServicesStartupStep("bridge-probe", directBridgeProbe.Detail), cancellationToken);
            if (directBridgeProbe.State == CodingServicesProbeState.Healthy)
            {
                activeTransport = CodingServicesTransportKind.DirectBridge;
            }
        }

        return new CodingServicesSessionStartupReport(
            paths.SiteUrl,
            paths.ServerName,
            paths.SettingsPath,
            paths.DirectBridgeCommand,
            stoppedProcesses,
            startedProcessId,
            siteProbe,
            nativeMount,
            nativeActivation,
            threadReset,
            directBridgeProbe,
            steps,
            activeTransport);
    }

    private static async Task AddStepAsync(
        List<CodingServicesStartupStep> steps,
        Func<CodingServicesStartupStep, CancellationToken, Task>? onStepAsync,
        CodingServicesStartupStep step,
        CancellationToken cancellationToken)
    {
        steps.Add(step);
        if (onStepAsync is not null)
        {
            await onStepAsync(step, cancellationToken);
        }
    }
}

public sealed class CodingServicesSessionStartupPlugin
{
    private readonly CodingServicesSessionStartupWorkflow workflow;
    private readonly Func<CodingServicesStartupStep, CancellationToken, Task>? onStepAsync;

    public CodingServicesSessionStartupPlugin()
        : this(
            new CodingServicesSessionStartupWorkflow(
                new LocalCodingServicesSessionStartupRuntime(),
                new NullCodingServicesSessionHostController()),
            null)
    {
    }

    public CodingServicesSessionStartupPlugin(
        CodingServicesSessionStartupWorkflow workflow,
        Func<CodingServicesStartupStep, CancellationToken, Task>? onStepAsync = null)
    {
        this.workflow = workflow;
        this.onStepAsync = onStepAsync;
    }

    [KernelFunction("initialize_coding_services")]
    [Description("Restarts CodexUI on http://localhost:5000, probes native mounted MCP availability, and falls back to the direct bridge when needed.")]
    public async Task<string> InitializeCodingServicesAsync(
        [Description("The CodingServices repository root.")] string repositoryRoot,
        [Description("Optional explicit appsettings.json path.")] string? settingsPath = null,
        [Description("Optional explicit site URL. Defaults to http://localhost:5000/.")] string siteUrl = "http://localhost:5000/",
        [Description("Whether to stop already-running CodexUI processes before starting.")] bool stopExistingProcesses = true,
        [Description("Whether to start CodexUI after any stop step.")] bool startCodexUi = true,
        [Description("Whether to ask the host to reset/remount the thread before direct bridge fallback.")] bool attemptThreadReset = true,
        CancellationToken cancellationToken = default)
    {
        CodingServicesSessionStartupReport report = await workflow.InitializeAsync(
            new CodingServicesSessionStartupRequest(
                repositoryRoot,
                settingsPath,
                siteUrl,
                stopExistingProcesses,
                startCodexUi,
                attemptThreadReset),
            onStepAsync,
            cancellationToken);
        return report.ToUserMessage();
    }
}
