namespace CodexUI.Models;

public sealed record CodexUsageSummaryViewModel(
    string State,
    string CodexHomePath,
    int SessionFileCount,
    int UsageEventCount,
    long TotalTokens,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    long LastTurnInputTokens,
    long LastTurnCachedInputTokens,
    long LastTurnOutputTokens,
    long LastTurnReasoningOutputTokens,
    long LastTurnTokens,
    DateTimeOffset? FirstUsageAt,
    int? FiveHourUsedPercent,
    DateTimeOffset? FiveHourResetsAt,
    int? WeeklyUsedPercent,
    DateTimeOffset? WeeklyResetsAt,
    string? PlanType,
    DateTimeOffset? LastUsageAt)
{
    public int? FiveHourRemainingPercent =>
        FiveHourUsedPercent is null ? null : Math.Max(0, 100 - FiveHourUsedPercent.Value);

    public int? WeeklyRemainingPercent =>
        WeeklyUsedPercent is null ? null : Math.Max(0, 100 - WeeklyUsedPercent.Value);

    public string LastUsageLabel =>
        LastUsageAt?.ToLocalTime().ToString("g") ?? "No usage events found";

    public string FirstUsageLabel =>
        FirstUsageAt?.ToLocalTime().ToString("g") ?? "No usage events found";

    public long FreshInputTokens =>
        Math.Max(0, InputTokens - CachedInputTokens);

    public long LastTurnFreshInputTokens =>
        Math.Max(0, LastTurnInputTokens - LastTurnCachedInputTokens);

    public string SessionSpanLabel =>
        GetSessionSpanLabel();

    public string FiveHourLabel =>
        FiveHourRemainingPercent is null ? "Not reported" : $"{FiveHourRemainingPercent}% remaining";

    public string WeeklyLabel =>
        WeeklyRemainingPercent is null ? "Not reported" : $"{WeeklyRemainingPercent}% remaining";

    public string FiveHourResetLabel =>
        FiveHourResetsAt?.ToLocalTime().ToString("g") ?? "Unknown reset";

    public string WeeklyResetLabel =>
        WeeklyResetsAt?.ToLocalTime().ToString("g") ?? "Unknown reset";

    public static CodexUsageSummaryViewModel Empty { get; } = new(
        "Not scanned",
        string.Empty,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    private string GetSessionSpanLabel()
    {
        if (FirstUsageAt is null || LastUsageAt is null)
        {
            return "Unknown";
        }

        TimeSpan span = LastUsageAt.Value - FirstUsageAt.Value;
        if (span < TimeSpan.Zero)
        {
            return "Unknown";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        return $"{Math.Max(0, span.Minutes)}m";
    }
}
