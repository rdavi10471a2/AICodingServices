using System.Text.Json;

namespace AICodingServices.Workflow;

public static class CodingServicesMcpBridgeProtocol
{
    public const string ContentLengthHeaderName = "Content-Length";
    public const string ProtocolVersion = "2025-03-26";
    public const int InitializeRequestId = 1;
    public const int MonitorStatusRequestId = 2;
    public const string InitializeMethod = "initialize";
    public const string InitializedNotificationMethod = "notifications/initialized";
    public const string ToolsCallMethod = "tools/call";
    public const string MonitorStatusToolName = "get_monitor_status";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string CreateInitializeRequestJson()
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = InitializeRequestId,
            method = InitializeMethod,
            @params = new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { },
                clientInfo = new
                {
                    name = "CodingServicesSessionStartup",
                    version = "1.0.0"
                }
            }
        }, JsonOptions);
    }

    public static string CreateInitializedNotificationJson()
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = InitializedNotificationMethod,
            @params = new { }
        }, JsonOptions);
    }

    public static string CreateMonitorStatusToolCallJson()
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = MonitorStatusRequestId,
            method = ToolsCallMethod,
            @params = new
            {
                name = MonitorStatusToolName,
                arguments = new { }
            }
        }, JsonOptions);
    }
}
