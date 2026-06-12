namespace AICodingServices.Logging.Tests;

public sealed class MonitorLogPipeTests
{
    [Fact]
    public async Task Client_sends_entries_to_server_sink()
    {
        string pipeName = "AICodingServices.Test." + Guid.NewGuid().ToString("N");
        CapturingLogger sink = new();
        using MonitorLogPipeServer server = new(pipeName, sink);
        server.Start();
        MonitorLogPipeClientLogger client = new(
            pipeName,
            new JsonLinesMonitorLogger(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "fallback.ndjson")),
            TimeSpan.FromSeconds(2));

        client.Write(
            MonitorLogLevel.Information,
            "AICodingServices.Cli",
            "adapter.query.started",
            "CLI adapter query started.",
            new Dictionary<string, string>
            {
                ["commandLine"] = "status --repo-root ."
            });

        await sink.WaitForEntryAsync();
        MonitorLogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal("adapter.query.started", entry.EventName);
        Assert.Equal("status --repo-root .", entry.Properties["commandLine"]);
    }

    [Fact]
    public void Client_fallback_writes_hub_unavailable_warning_once_per_burst()
    {
        CapturingLogger fallback = new();
        MonitorLogPipeClientLogger client = new(
            "AICodingServices.Test." + Guid.NewGuid().ToString("N"),
            fallback,
            TimeSpan.FromMilliseconds(10));

        client.Write(MonitorLogLevel.Information, "AICodingServices.Cli", "event.one", "one");
        client.Write(MonitorLogLevel.Information, "AICodingServices.Cli", "event.two", "two");

        Assert.Equal(3, fallback.Entries.Count);
        Assert.Equal("adapter.hub.unavailable", fallback.Entries[0].EventName);
        Assert.Equal("event.one", fallback.Entries[1].EventName);
        Assert.Equal("event.two", fallback.Entries[2].EventName);
    }

    [Fact]
    public void Mcp_traffic_policy_skips_discovery_methods_and_trims_large_payloads()
    {
        Assert.False(McpTrafficLogPolicy.ShouldLogTraffic("initialize", null));
        Assert.False(McpTrafficLogPolicy.ShouldLogTraffic("tools/list", null));
        Assert.False(McpTrafficLogPolicy.ShouldLogTraffic("resources/list", null));
        Assert.True(McpTrafficLogPolicy.ShouldLogTraffic("tools/call", "refresh_file"));

        Dictionary<string, string> properties = new();
        string largePayload = new('a', 3000);
        McpTrafficLogPolicy.AddPayloadProperties(properties, "contentText", largePayload);

        Assert.Equal(500, properties["contentTextPreview"].Length);
        Assert.Equal("3000", properties["contentTextLength"]);
        Assert.Equal("true", properties["contentTextTruncated"]);
        Assert.False(properties.ContainsKey("contentText"));
        Assert.True(properties.ContainsKey("contentTextSha256"));
    }

    private sealed class CapturingLogger : IMonitorLogger
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<MonitorLogEntry> Entries { get; } = [];

        public void Write(
            MonitorLogLevel level,
            string source,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? properties = null)
        {
            Entries.Add(new MonitorLogEntry(
                DateTimeOffset.UtcNow,
                level,
                source,
                eventName,
                message,
                properties ?? new Dictionary<string, string>()));
            completion.TrySetResult();
        }

        public async Task WaitForEntryAsync()
        {
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(completion.Task, finished);
        }
    }
}
