namespace CodexUI.Models;

public sealed record McpServerViewModel(
    string State,
    string StateCssClass,
    string ProcessLabel,
    string Transport,
    string StartedLabel,
    string Detail)
{
    public static McpServerViewModel NotConnected { get; } = new(
        "Stopped",
        "state-stopped",
        "No process",
        "stdio",
        "Not started",
        "The AICodingServices MCP hub has not started yet.");
}
