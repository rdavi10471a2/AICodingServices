using CodexUI.Models;
using CodexUI.Services;

namespace CodexUI.Tests;

public sealed class CodexUsageSummaryServiceTests
{
    [Fact]
    public void ReadUsageEvent_extracts_aggregate_token_and_limit_values()
    {
        string line = """
            {"timestamp":"2026-06-12T19:45:27.499Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":700,"output_tokens":120,"reasoning_output_tokens":20,"total_tokens":1120},"last_token_usage":{"input_tokens":300,"cached_input_tokens":200,"output_tokens":50,"reasoning_output_tokens":10,"total_tokens":350},"model_context_window":258400},"rate_limits":{"limit_id":"codex","primary":{"used_percent":2,"window_minutes":300,"resets_at":1781311515},"secondary":{"used_percent":15,"window_minutes":10080,"resets_at":1781878914},"plan_type":"prolite"}}}
            """;

        bool parsed = CodexUsageSummaryService.TryReadUsageEvent(
            line,
            out CodexUsageSummaryService.CodexUsageLogEntry? usageEvent);

        Assert.True(parsed);
        Assert.NotNull(usageEvent);
        Assert.Equal(1120, usageEvent.TotalTokens);
        Assert.Equal(1000, usageEvent.InputTokens);
        Assert.Equal(700, usageEvent.CachedInputTokens);
        Assert.Equal(120, usageEvent.OutputTokens);
        Assert.Equal(20, usageEvent.ReasoningOutputTokens);
        Assert.Equal(300, usageEvent.LastTurnInputTokens);
        Assert.Equal(200, usageEvent.LastTurnCachedInputTokens);
        Assert.Equal(50, usageEvent.LastTurnOutputTokens);
        Assert.Equal(10, usageEvent.LastTurnReasoningOutputTokens);
        Assert.Equal(350, usageEvent.LastTurnTokens);
        Assert.Equal(2, usageEvent.FiveHourUsedPercent);
        Assert.Equal(15, usageEvent.WeeklyUsedPercent);
        Assert.Equal("prolite", usageEvent.PlanType);
        Assert.Equal(DateTimeOffset.Parse("2026-06-12T19:45:27.499Z"), usageEvent.Timestamp);
    }

    [Fact]
    public void ReadUsageEvent_ignores_non_usage_messages()
    {
        string line = """
            {"timestamp":"2026-06-12T19:45:27.448Z","type":"event_msg","payload":{"type":"agent_message","message":"do not persist this text"}}
            """;

        bool parsed = CodexUsageSummaryService.TryReadUsageEvent(
            line,
            out CodexUsageSummaryService.CodexUsageLogEntry? usageEvent);

        Assert.False(parsed);
        Assert.Null(usageEvent);
    }

    [Fact]
    public void ReadUsageEvent_allows_null_rate_limit_sections()
    {
        string line = """
            {"timestamp":"2026-06-12T19:45:27.499Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":700,"output_tokens":120,"reasoning_output_tokens":20,"total_tokens":1120},"last_token_usage":{"total_tokens":350}},"rate_limits":{"primary":null,"secondary":null,"plan_type":"prolite"}}}
            """;

        bool parsed = CodexUsageSummaryService.TryReadUsageEvent(
            line,
            out CodexUsageSummaryService.CodexUsageLogEntry? usageEvent);

        Assert.True(parsed);
        Assert.NotNull(usageEvent);
        Assert.Equal(1120, usageEvent.TotalTokens);
        Assert.Null(usageEvent.FiveHourUsedPercent);
        Assert.Null(usageEvent.WeeklyUsedPercent);
        Assert.Equal("prolite", usageEvent.PlanType);
    }

    [Fact]
    public void GetSummary_uses_latest_snapshot_from_newest_five_session_files()
    {
        string codexHomePath = Path.Combine(Path.GetTempPath(), $"codex-usage-test-{Guid.NewGuid():N}");
        try
        {
            string sessionsPath = Path.Combine(codexHomePath, "sessions", "2026", "06", "12");
            Directory.CreateDirectory(sessionsPath);

            for (int index = 0; index < 6; index++)
            {
                string path = Path.Combine(sessionsPath, $"session-{index}.jsonl");
                string timestamp = index == 5
                    ? "2026-06-12T19:45:27.499Z"
                    : "2026-02-27T21:04:47.000Z";
                int usedPercent = index == 5 ? 2 : 98;
                long totalTokens = index == 5 ? 1120 : 999999;
                File.WriteAllText(path, CreateUsageLine(timestamp, usedPercent, totalTokens));
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 6, 12, 12, index, 0, DateTimeKind.Utc));
            }

            CodexUsageSummaryService service = new(codexHomePath);

            CodexUsageSummaryViewModel summary = service.GetSummary();

            Assert.Equal(5, summary.SessionFileCount);
            Assert.Equal(1120, summary.TotalTokens);
            Assert.Equal(2, summary.FiveHourUsedPercent);
            Assert.Equal(DateTimeOffset.Parse("2026-02-27T21:04:47.000Z"), summary.FirstUsageAt);
            Assert.Equal(DateTimeOffset.Parse("2026-06-12T19:45:27.499Z"), summary.LastUsageAt);
        }
        finally
        {
            if (Directory.Exists(codexHomePath))
            {
                Directory.Delete(codexHomePath, true);
            }
        }
    }

    private static string CreateUsageLine(string timestamp, int usedPercent, long totalTokens)
    {
        string template = """
            {"timestamp":"__TIMESTAMP__","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":700,"output_tokens":120,"reasoning_output_tokens":20,"total_tokens":__TOTAL_TOKENS__},"last_token_usage":{"input_tokens":300,"cached_input_tokens":200,"output_tokens":50,"reasoning_output_tokens":10,"total_tokens":350}},"rate_limits":{"primary":{"used_percent":__USED_PERCENT__,"resets_at":1781311515},"secondary":{"used_percent":15,"resets_at":1781878914},"plan_type":"prolite"}}}
            """;
        return template
            .Replace("__TIMESTAMP__", timestamp, StringComparison.Ordinal)
            .Replace("__TOTAL_TOKENS__", totalTokens.ToString(), StringComparison.Ordinal)
            .Replace("__USED_PERCENT__", usedPercent.ToString(), StringComparison.Ordinal);
    }
}
