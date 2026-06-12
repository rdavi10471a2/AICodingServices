using System.Text.Json;
using CodexUI.Models;

namespace CodexUI.Services;

public sealed class CodexUsageSummaryService : ICodexUsageSummaryService
{
    private const int MaxSessionFiles = 5;
    private readonly string codexHomePath;

    public CodexUsageSummaryService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex"))
    {
    }

    public CodexUsageSummaryService(string codexHomePath)
    {
        this.codexHomePath = codexHomePath;
    }

    public CodexUsageSummaryViewModel GetSummary()
    {
        string sessionsPath = Path.Combine(codexHomePath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return CodexUsageSummaryViewModel.Empty with
            {
                State = "Codex sessions not found",
                CodexHomePath = codexHomePath
            };
        }

        FileInfo[] sessionFiles = new DirectoryInfo(sessionsPath)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxSessionFiles)
            .ToArray();

        UsageAccumulator accumulator = new();
        foreach (FileInfo sessionFile in sessionFiles)
        {
            ReadUsageEvents(sessionFile.FullName, accumulator);
        }

        string state = accumulator.UsageEventCount == 0
            ? "No usage events found"
            : "Local usage scanned";

        return new CodexUsageSummaryViewModel(
            state,
            codexHomePath,
            sessionFiles.Length,
            accumulator.UsageEventCount,
            accumulator.TotalTokens,
            accumulator.InputTokens,
            accumulator.CachedInputTokens,
            accumulator.OutputTokens,
            accumulator.ReasoningOutputTokens,
            accumulator.LastTurnInputTokens,
            accumulator.LastTurnCachedInputTokens,
            accumulator.LastTurnOutputTokens,
            accumulator.LastTurnReasoningOutputTokens,
            accumulator.LastTurnTokens,
            accumulator.FirstUsageAt,
            accumulator.FiveHourUsedPercent,
            accumulator.FiveHourResetsAt,
            accumulator.WeeklyUsedPercent,
            accumulator.WeeklyResetsAt,
            accumulator.PlanType,
            accumulator.LastUsageAt);
    }

    private static void ReadUsageEvents(string path, UsageAccumulator accumulator)
    {
        try
        {
            using (FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new(stream))
            {
                while (reader.ReadLine() is string line)
                {
                    if (!line.Contains("\"token_count\"", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (TryReadUsageEvent(line, out CodexUsageLogEntry? usageEvent)
                        && usageEvent is not null)
                    {
                        accumulator.Apply(usageEvent);
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static bool TryReadUsageEvent(string line, out CodexUsageLogEntry? usageEvent)
    {
        usageEvent = null;
        try
        {
            using (JsonDocument document = JsonDocument.Parse(line))
            {
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("payload", out JsonElement payload)
                    || !payload.TryGetProperty("type", out JsonElement payloadType)
                    || !payloadType.ValueEquals("token_count"))
                {
                    return false;
                }

                DateTimeOffset? timestamp = ReadTimestamp(root);
                long totalTokens = 0;
                long inputTokens = 0;
                long cachedInputTokens = 0;
                long outputTokens = 0;
                long reasoningOutputTokens = 0;
                long lastTurnInputTokens = 0;
                long lastTurnCachedInputTokens = 0;
                long lastTurnOutputTokens = 0;
                long lastTurnReasoningOutputTokens = 0;
                long lastTurnTokens = 0;

                if (payload.TryGetProperty("info", out JsonElement info))
                {
                    ReadTokenInfo(
                        info,
                        out totalTokens,
                        out inputTokens,
                        out cachedInputTokens,
                        out outputTokens,
                        out reasoningOutputTokens,
                        out lastTurnInputTokens,
                        out lastTurnCachedInputTokens,
                        out lastTurnOutputTokens,
                        out lastTurnReasoningOutputTokens,
                        out lastTurnTokens);
                }

                int? fiveHourUsedPercent = null;
                DateTimeOffset? fiveHourResetsAt = null;
                int? weeklyUsedPercent = null;
                DateTimeOffset? weeklyResetsAt = null;
                string? planType = null;
                if (payload.TryGetProperty("rate_limits", out JsonElement rateLimits))
                {
                    ReadRateLimits(
                        rateLimits,
                        out fiveHourUsedPercent,
                        out fiveHourResetsAt,
                        out weeklyUsedPercent,
                        out weeklyResetsAt,
                        out planType);
                }

                usageEvent = new CodexUsageLogEntry(
                    timestamp,
                    totalTokens,
                    inputTokens,
                    cachedInputTokens,
                    outputTokens,
                    reasoningOutputTokens,
                    lastTurnInputTokens,
                    lastTurnCachedInputTokens,
                    lastTurnOutputTokens,
                    lastTurnReasoningOutputTokens,
                    lastTurnTokens,
                    fiveHourUsedPercent,
                    fiveHourResetsAt,
                    weeklyUsedPercent,
                    weeklyResetsAt,
                    planType);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out JsonElement timestamp)
            && timestamp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(timestamp.GetString(), out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static void ReadTokenInfo(
        JsonElement info,
        out long totalTokens,
        out long inputTokens,
        out long cachedInputTokens,
        out long outputTokens,
        out long reasoningOutputTokens,
        out long lastTurnInputTokens,
        out long lastTurnCachedInputTokens,
        out long lastTurnOutputTokens,
        out long lastTurnReasoningOutputTokens,
        out long lastTurnTokens)
    {
        totalTokens = 0;
        inputTokens = 0;
        cachedInputTokens = 0;
        outputTokens = 0;
        reasoningOutputTokens = 0;
        lastTurnInputTokens = 0;
        lastTurnCachedInputTokens = 0;
        lastTurnOutputTokens = 0;
        lastTurnReasoningOutputTokens = 0;
        lastTurnTokens = 0;

        if (info.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (info.TryGetProperty("total_token_usage", out JsonElement totalUsage))
        {
            totalTokens = GetInt64(totalUsage, "total_tokens");
            inputTokens = GetInt64(totalUsage, "input_tokens");
            cachedInputTokens = GetInt64(totalUsage, "cached_input_tokens");
            outputTokens = GetInt64(totalUsage, "output_tokens");
            reasoningOutputTokens = GetInt64(totalUsage, "reasoning_output_tokens");
        }

        if (info.TryGetProperty("last_token_usage", out JsonElement lastUsage))
        {
            lastTurnInputTokens = GetInt64(lastUsage, "input_tokens");
            lastTurnCachedInputTokens = GetInt64(lastUsage, "cached_input_tokens");
            lastTurnOutputTokens = GetInt64(lastUsage, "output_tokens");
            lastTurnReasoningOutputTokens = GetInt64(lastUsage, "reasoning_output_tokens");
            lastTurnTokens = GetInt64(lastUsage, "total_tokens");
        }
    }

    private static void ReadRateLimits(
        JsonElement rateLimits,
        out int? fiveHourUsedPercent,
        out DateTimeOffset? fiveHourResetsAt,
        out int? weeklyUsedPercent,
        out DateTimeOffset? weeklyResetsAt,
        out string? planType)
    {
        planType = GetString(rateLimits, "plan_type");
        fiveHourUsedPercent = null;
        fiveHourResetsAt = null;
        weeklyUsedPercent = null;
        weeklyResetsAt = null;

        if (rateLimits.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (rateLimits.TryGetProperty("primary", out JsonElement primary))
        {
            fiveHourUsedPercent = GetInt32(primary, "used_percent");
            fiveHourResetsAt = ReadUnixTimestamp(primary, "resets_at");
        }

        if (rateLimits.TryGetProperty("secondary", out JsonElement secondary))
        {
            weeklyUsedPercent = GetInt32(secondary, "used_percent");
            weeklyResetsAt = ReadUnixTimestamp(secondary, "resets_at");
        }
    }

    private static long GetInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetInt64(out long value))
        {
            return value;
        }

        return 0;
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetInt32(out int value))
        {
            return value;
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static DateTimeOffset? ReadUnixTimestamp(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetInt64(out long value))
        {
            return DateTimeOffset.FromUnixTimeSeconds(value);
        }

        return null;
    }

    public sealed record CodexUsageLogEntry(
        DateTimeOffset? Timestamp,
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
        int? FiveHourUsedPercent,
        DateTimeOffset? FiveHourResetsAt,
        int? WeeklyUsedPercent,
        DateTimeOffset? WeeklyResetsAt,
        string? PlanType);

    private sealed class UsageAccumulator
    {
        public int UsageEventCount { get; set; }

        public long TotalTokens { get; set; }

        public long InputTokens { get; set; }

        public long CachedInputTokens { get; set; }

        public long OutputTokens { get; set; }

        public long ReasoningOutputTokens { get; set; }

        public long LastTurnInputTokens { get; set; }

        public long LastTurnCachedInputTokens { get; set; }

        public long LastTurnOutputTokens { get; set; }

        public long LastTurnReasoningOutputTokens { get; set; }

        public long LastTurnTokens { get; set; }

        public DateTimeOffset? FirstUsageAt { get; set; }

        public int? FiveHourUsedPercent { get; set; }

        public DateTimeOffset? FiveHourResetsAt { get; set; }

        public int? WeeklyUsedPercent { get; set; }

        public DateTimeOffset? WeeklyResetsAt { get; set; }

        public string? PlanType { get; set; }

        public DateTimeOffset? LastUsageAt { get; set; }

        public void Apply(CodexUsageLogEntry usageEvent)
        {
            UsageEventCount++;
            ApplySessionBounds(usageEvent);

            if (IsOlderThanCurrentSnapshot(usageEvent))
            {
                return;
            }

            TotalTokens = usageEvent.TotalTokens;
            InputTokens = usageEvent.InputTokens;
            CachedInputTokens = usageEvent.CachedInputTokens;
            OutputTokens = usageEvent.OutputTokens;
            ReasoningOutputTokens = usageEvent.ReasoningOutputTokens;
            LastTurnInputTokens = usageEvent.LastTurnInputTokens;
            LastTurnCachedInputTokens = usageEvent.LastTurnCachedInputTokens;
            LastTurnOutputTokens = usageEvent.LastTurnOutputTokens;
            LastTurnReasoningOutputTokens = usageEvent.LastTurnReasoningOutputTokens;
            LastTurnTokens = usageEvent.LastTurnTokens;
            FiveHourUsedPercent = usageEvent.FiveHourUsedPercent;
            FiveHourResetsAt = usageEvent.FiveHourResetsAt;
            WeeklyUsedPercent = usageEvent.WeeklyUsedPercent;
            WeeklyResetsAt = usageEvent.WeeklyResetsAt;
            PlanType = usageEvent.PlanType ?? PlanType;
            LastUsageAt = usageEvent.Timestamp ?? LastUsageAt;
        }

        private void ApplySessionBounds(CodexUsageLogEntry usageEvent)
        {
            if (usageEvent.Timestamp is null)
            {
                return;
            }

            if (FirstUsageAt is null || usageEvent.Timestamp < FirstUsageAt)
            {
                FirstUsageAt = usageEvent.Timestamp;
            }
        }

        private bool IsOlderThanCurrentSnapshot(CodexUsageLogEntry usageEvent)
        {
            if (LastUsageAt is null)
            {
                return false;
            }

            return usageEvent.Timestamp is null
                || usageEvent.Timestamp < LastUsageAt;
        }
    }
}
