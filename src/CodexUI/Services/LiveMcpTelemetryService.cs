namespace CodexUI.Services;

using AICodingServices.McpHub;
using CodexUI.Models;

public sealed class LiveMcpTelemetryService : IMcpTelemetrySink, ITelemetryViewService
{
    private const int MaxEvents = 200;

    private readonly object syncRoot = new();
    private readonly Queue<TelemetryEventViewModel> events = new(MaxEvents);

    public void Record(McpTelemetryEvent telemetryEvent)
    {
        TelemetryEventViewModel viewEvent = MapEvent(telemetryEvent);
        lock (syncRoot)
        {
            if (events.Count == MaxEvents)
            {
                events.Dequeue();
            }

            events.Enqueue(viewEvent);
        }
    }

    public TelemetryViewModel GetTelemetry()
    {
        TelemetryEventViewModel[] snapshot;
        lock (syncRoot)
        {
            snapshot = events.ToArray();
        }

        if (snapshot.Length == 0)
        {
            return TelemetryViewModel.Empty;
        }

        TelemetryEventViewModel[] recentEvents = snapshot
            .OrderByDescending(entry => entry.TimestampUtc)
            .ToArray();

        int requestCount = snapshot.Count(entry => entry.EventName.Equals("adapter.mcp.request.started", StringComparison.OrdinalIgnoreCase));
        int responseCount = snapshot.Count(entry => entry.EventName.Equals("adapter.mcp.request.completed", StringComparison.OrdinalIgnoreCase));
        int errorCount = snapshot.Count(entry => entry.IsError);
        DateTimeOffset lastEventAt = snapshot.Max(entry => entry.TimestampUtc);

        return new TelemetryViewModel(
            "Live MCP traffic captured",
            "Live CodexUI hub stream",
            snapshot.Length,
            requestCount,
            responseCount,
            errorCount,
            lastEventAt,
            recentEvents);
    }

    private static TelemetryEventViewModel MapEvent(McpTelemetryEvent telemetryEvent)
    {
        IReadOnlyDictionary<string, string> properties = telemetryEvent.Properties;
        string eventName = telemetryEvent.EventName;
        string direction = eventName.EndsWith("started", StringComparison.OrdinalIgnoreCase)
            ? "Request"
            : eventName.EndsWith("completed", StringComparison.OrdinalIgnoreCase)
                ? "Response"
                : "Event";

        return new TelemetryEventViewModel(
            telemetryEvent.TimestampUtc,
            direction,
            eventName,
            Get(properties, "sessionId"),
            Get(properties, "requestId"),
            Get(properties, "method"),
            Get(properties, "toolName"),
            bool.TryParse(Get(properties, "isError"), out bool isError) && isError,
            TryGetLong(properties, "durationMs"),
            TryGetLong(properties, "requestBytes"),
            TryGetLong(properties, "messageBytes"),
            FirstNonEmpty(
                Get(properties, "contentTextPreview"),
                Get(properties, "argumentsPreview"),
                Get(properties, "stderr"),
                telemetryEvent.Message));
    }

    private static string Get(IReadOnlyDictionary<string, string> properties, string key)
    {
        return properties.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    private static long? TryGetLong(IReadOnlyDictionary<string, string> properties, string key)
    {
        return long.TryParse(Get(properties, key), out long value) ? value : null;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
