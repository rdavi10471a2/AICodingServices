namespace AICodingServices.McpHub;

public interface IMcpTelemetrySink
{
    void Record(McpTelemetryEvent telemetryEvent);
}
