namespace AICodingServices.McpHub;

using AICodingServices.Logging;

public sealed record McpTelemetryEvent(
    DateTimeOffset TimestampUtc,
    MonitorLogLevel Level,
    string EventName,
    string Message,
    IReadOnlyDictionary<string, string> Properties);
