namespace CodexUI.Models;

public sealed record TelemetryViewModel(
    string State,
    string SourceLabel,
    int EventCount,
    int RequestCount,
    int ResponseCount,
    int ErrorCount,
    DateTimeOffset? LastEventAtUtc,
    IReadOnlyList<TelemetryEventViewModel> Events)
{
    public string LastEventLabel =>
        LastEventAtUtc?.ToLocalTime().ToString("g") ?? "No MCP events captured";

    public static TelemetryViewModel Empty { get; } = new(
        "Waiting for MCP traffic",
        "Live CodexUI hub stream",
        0,
        0,
        0,
        0,
        null,
        []);
}

public sealed record TelemetryEventViewModel(
    DateTimeOffset TimestampUtc,
    string Direction,
    string EventName,
    string SessionId,
    string RequestId,
    string Method,
    string ToolName,
    bool IsError,
    long? DurationMs,
    long? RequestBytes,
    long? MessageBytes,
    string Preview)
{
    public string TimestampLabel => TimestampUtc.ToLocalTime().ToString("T");

    public string ToolLabel => string.IsNullOrWhiteSpace(ToolName) ? Method : ToolName;

    public string DurationLabel => DurationMs is null ? string.Empty : DurationMs.Value.ToString("N0") + " ms";

    public string SizeLabel
    {
        get
        {
            if (RequestBytes is not null && MessageBytes is not null)
            {
                return RequestBytes.Value.ToString("N0") + " / " + MessageBytes.Value.ToString("N0") + " bytes";
            }

            if (RequestBytes is not null)
            {
                return RequestBytes.Value.ToString("N0") + " bytes";
            }

            return MessageBytes is null ? string.Empty : MessageBytes.Value.ToString("N0") + " bytes";
        }
    }
}
