using AICodingServices.Workflow;

namespace AICodingServices.Workflow.Tests;

public sealed class CodingServicesSessionStartupWorkflowTests
{
    [Fact]
    public async Task InitializeAsync_prefers_native_mounted_transport_when_visible_and_activatable()
    {
        FakeRuntime runtime = new()
        {
            SiteProbe = new CodingServicesProbeResult(CodingServicesProbeState.Healthy, "Site is healthy."),
            DirectBridgeProbe = new CodingServicesDirectBridgeProbeResult(CodingServicesProbeState.Healthy, "Bridge is healthy.", "monitor")
        };
        runtime.RunningProcesses.Add(new CodingServicesProcessDescriptor(42, "CodexUI", "existing"));

        FakeHostController host = new()
        {
            NativeStatuses = new Queue<CodingServicesNativeMountResult>(new[]
            {
                new CodingServicesNativeMountResult(CodingServicesNativeMountState.Visible, "Mounted server is visible.")
            }),
            ActivationResult = CodingServicesHostCommandResult.Success("Mounted server is active."),
            ThreadResetResult = CodingServicesHostCommandResult.Unsupported("Reset not required.")
        };

        CodingServicesSessionStartupWorkflow workflow = new(runtime, host);

        CodingServicesSessionStartupReport report = await workflow.InitializeAsync(
            new CodingServicesSessionStartupRequest(@"C:\Repo"));

        Assert.Equal(CodingServicesTransportKind.NativeMounted, report.ActiveTransport);
        Assert.Empty(report.StoppedProcesses);
        Assert.Empty(runtime.StoppedProcessIds);
        Assert.Equal(0, runtime.StartCalls);
        Assert.Equal(0, runtime.DirectBridgeCalls);
        Assert.Equal(1, host.ActivationCalls);
        Assert.Equal(0, host.ThreadResetCalls);
    }

    [Fact]
    public async Task InitializeAsync_attempts_thread_reset_before_falling_back()
    {
        FakeRuntime runtime = new()
        {
            SiteProbe = new CodingServicesProbeResult(CodingServicesProbeState.Healthy, "Site is healthy."),
            DirectBridgeProbe = new CodingServicesDirectBridgeProbeResult(CodingServicesProbeState.Healthy, "Bridge is healthy.", "monitor")
        };

        FakeHostController host = new()
        {
            NativeStatuses = new Queue<CodingServicesNativeMountResult>(new[]
            {
                new CodingServicesNativeMountResult(CodingServicesNativeMountState.Missing, "Mounted server is missing."),
                new CodingServicesNativeMountResult(CodingServicesNativeMountState.Visible, "Mounted server is visible after reset.")
            }),
            ActivationResult = CodingServicesHostCommandResult.Success("Mounted server is active."),
            ThreadResetResult = CodingServicesHostCommandResult.Success("Thread reset completed.")
        };

        CodingServicesSessionStartupWorkflow workflow = new(runtime, host);

        CodingServicesSessionStartupReport report = await workflow.InitializeAsync(
            new CodingServicesSessionStartupRequest(@"C:\Repo"));

        Assert.Equal(CodingServicesTransportKind.NativeMounted, report.ActiveTransport);
        Assert.Equal(1, host.ThreadResetCalls);
        Assert.Equal(1, host.ActivationCalls);
        Assert.Equal(0, runtime.DirectBridgeCalls);
        Assert.Equal(0, runtime.StartCalls);
    }

    [Fact]
    public async Task InitializeAsync_falls_back_to_direct_bridge_when_native_mount_is_unavailable()
    {
        FakeRuntime runtime = new()
        {
            SiteProbe = new CodingServicesProbeResult(CodingServicesProbeState.Healthy, "Site is healthy."),
            DirectBridgeProbe = new CodingServicesDirectBridgeProbeResult(CodingServicesProbeState.Healthy, "Bridge is healthy.", "monitor")
        };

        FakeHostController host = new()
        {
            NativeStatuses = new Queue<CodingServicesNativeMountResult>(new[]
            {
                new CodingServicesNativeMountResult(CodingServicesNativeMountState.Missing, "Mounted server is missing.")
            }),
            ActivationResult = CodingServicesHostCommandResult.Unsupported("Activation unavailable."),
            ThreadResetResult = CodingServicesHostCommandResult.Unsupported("Thread reset unavailable.")
        };

        CodingServicesSessionStartupWorkflow workflow = new(runtime, host);

        CodingServicesSessionStartupReport report = await workflow.InitializeAsync(
            new CodingServicesSessionStartupRequest(@"C:\Repo"));

        Assert.Equal(CodingServicesTransportKind.DirectBridge, report.ActiveTransport);
        Assert.Equal(1, runtime.DirectBridgeCalls);
        Assert.Equal(1, host.ThreadResetCalls);
        Assert.Equal(0, host.ActivationCalls);
        Assert.Equal(0, runtime.StartCalls);
    }

    [Fact]
    public async Task InitializeAsync_reports_no_active_transport_when_bridge_fails_and_native_mount_cannot_be_used()
    {
        FakeRuntime runtime = new()
        {
            SiteProbe = new CodingServicesProbeResult(CodingServicesProbeState.Healthy, "Site is healthy."),
            DirectBridgeProbe = new CodingServicesDirectBridgeProbeResult(CodingServicesProbeState.Failed, "Bridge failed.", null)
        };

        FakeHostController host = new()
        {
            NativeStatuses = new Queue<CodingServicesNativeMountResult>(new[]
            {
                new CodingServicesNativeMountResult(CodingServicesNativeMountState.Unsupported, "Mounted server state is unavailable.")
            }),
            ActivationResult = CodingServicesHostCommandResult.Unsupported("Activation unavailable."),
            ThreadResetResult = CodingServicesHostCommandResult.Unsupported("Thread reset unavailable.")
        };

        CodingServicesSessionStartupWorkflow workflow = new(runtime, host);

        CodingServicesSessionStartupReport report = await workflow.InitializeAsync(
            new CodingServicesSessionStartupRequest(@"C:\Repo"));

        Assert.Equal(CodingServicesTransportKind.None, report.ActiveTransport);
        Assert.Contains("host", report.ToUserMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runtime.StartCalls);
    }

    [Fact]
    public async Task InitializeAsync_restarts_only_when_site_probe_fails()
    {
        FakeRuntime runtime = new()
        {
            ProbeSequence = new Queue<CodingServicesProbeResult>(new[]
            {
                new CodingServicesProbeResult(CodingServicesProbeState.Failed, "Site is down."),
                new CodingServicesProbeResult(CodingServicesProbeState.Healthy, "Site recovered.")
            }),
            DirectBridgeProbe = new CodingServicesDirectBridgeProbeResult(CodingServicesProbeState.Healthy, "Bridge is healthy.", "monitor")
        };
        runtime.RunningProcesses.Add(new CodingServicesProcessDescriptor(42, "CodexUI", "existing"));

        FakeHostController host = new()
        {
            NativeStatuses = new Queue<CodingServicesNativeMountResult>(new[]
            {
                new CodingServicesNativeMountResult(CodingServicesNativeMountState.Missing, "Mounted server is missing.")
            }),
            ActivationResult = CodingServicesHostCommandResult.Unsupported("Activation unavailable."),
            ThreadResetResult = CodingServicesHostCommandResult.Unsupported("Thread reset unavailable.")
        };

        CodingServicesSessionStartupWorkflow workflow = new(runtime, host);

        CodingServicesSessionStartupReport report = await workflow.InitializeAsync(
            new CodingServicesSessionStartupRequest(@"C:\Repo"));

        Assert.Equal(CodingServicesTransportKind.DirectBridge, report.ActiveTransport);
        Assert.Single(report.StoppedProcesses);
        Assert.Equal([42], runtime.StoppedProcessIds);
        Assert.Equal(1, runtime.StartCalls);
        Assert.Equal(2, runtime.SiteProbeCalls);
    }

    private sealed class FakeRuntime : ICodingServicesSessionStartupRuntime
    {
        public List<CodingServicesProcessDescriptor> RunningProcesses { get; } = [];
        public List<int> StoppedProcessIds { get; } = [];
        public int StartCalls { get; private set; }
        public int DirectBridgeCalls { get; private set; }
        public int SiteProbeCalls { get; private set; }
        public Queue<CodingServicesProbeResult>? ProbeSequence { get; set; }
        public CodingServicesProbeResult SiteProbe { get; set; } =
            new(CodingServicesProbeState.Healthy, "Site is healthy.");
        public CodingServicesDirectBridgeProbeResult DirectBridgeProbe { get; set; } =
            new(CodingServicesProbeState.Healthy, "Bridge is healthy.", "monitor");

        public Task<IReadOnlyList<CodingServicesProcessDescriptor>> FindRunningCodexUiProcessesAsync(
            CodingServicesStartupPaths paths,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CodingServicesProcessDescriptor>>(RunningProcesses);
        }

        public Task<CodingServicesDirectBridgeProbeResult> ProbeDirectBridgeAsync(
            CodingServicesStartupPaths paths,
            CancellationToken cancellationToken)
        {
            DirectBridgeCalls++;
            return Task.FromResult(DirectBridgeProbe);
        }

        public Task<CodingServicesProbeResult> ProbeSiteAsync(string siteUrl, CancellationToken cancellationToken)
        {
            SiteProbeCalls++;
            if (ProbeSequence is not null && ProbeSequence.Count > 0)
            {
                return Task.FromResult(ProbeSequence.Dequeue());
            }

            return Task.FromResult(SiteProbe);
        }

        public Task<int?> StartCodexUiAsync(CodingServicesStartupPaths paths, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.FromResult<int?>(1234);
        }

        public Task StopProcessAsync(int processId, CancellationToken cancellationToken)
        {
            StoppedProcessIds.Add(processId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHostController : ICodingServicesSessionHostController
    {
        public Queue<CodingServicesNativeMountResult> NativeStatuses { get; init; } = new();
        public CodingServicesHostCommandResult ActivationResult { get; set; } =
            CodingServicesHostCommandResult.Unsupported("Activation unavailable.");
        public CodingServicesHostCommandResult ThreadResetResult { get; set; } =
            CodingServicesHostCommandResult.Unsupported("Thread reset unavailable.");
        public int ActivationCalls { get; private set; }
        public int ThreadResetCalls { get; private set; }

        public Task<CodingServicesHostCommandResult> ActivateNativeMountedServerAsync(string serverName, CancellationToken cancellationToken)
        {
            ActivationCalls++;
            return Task.FromResult(ActivationResult);
        }

        public Task<CodingServicesNativeMountResult> GetNativeMountStatusAsync(string serverName, CancellationToken cancellationToken)
        {
            if (NativeStatuses.Count == 0)
            {
                return Task.FromResult(new CodingServicesNativeMountResult(
                    CodingServicesNativeMountState.Unknown,
                    "No fake native status was configured."));
            }

            return Task.FromResult(NativeStatuses.Dequeue());
        }

        public Task<CodingServicesHostCommandResult> ResetThreadAsync(CancellationToken cancellationToken)
        {
            ThreadResetCalls++;
            return Task.FromResult(ThreadResetResult);
        }
    }
}
