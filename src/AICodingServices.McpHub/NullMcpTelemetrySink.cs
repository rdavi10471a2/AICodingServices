namespace AICodingServices.McpHub;

internal sealed class NullMcpTelemetrySink : IMcpTelemetrySink
{
    public static NullMcpTelemetrySink Instance { get; } = new();

    private NullMcpTelemetrySink()
    {
    }

    public void Record(McpTelemetryEvent telemetryEvent)
    {
    }
}
