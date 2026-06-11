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
