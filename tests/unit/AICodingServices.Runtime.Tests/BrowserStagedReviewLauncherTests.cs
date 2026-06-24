using AICodingServices.Workflow;

namespace AICodingServices.Runtime.Tests;

public sealed class BrowserStagedReviewLauncherTests
{
    [Fact]
    public void BuildReviewUrl_uses_session_route_for_session_records()
    {
        StagedEditRecord record = new()
        {
            StagedRecordId = "staged with spaces",
            SessionId = "session/one"
        };

        string url = BrowserStagedReviewLauncher.BuildReviewUrl("http://localhost:5000/", record);

        Assert.Equal("http://localhost:5000/review/session/session%2Fone", url);
    }

    [Fact]
    public void BuildReviewUrl_uses_staged_route_for_single_record()
    {
        string url = BrowserStagedReviewLauncher.BuildReviewUrl(
            "http://localhost:5000/",
            "staged one");

        Assert.Equal("http://localhost:5000/review/staged/staged%20one", url);
    }

    [Fact]
    public void Launch_returns_review_url_without_starting_process_when_disabled()
    {
        string? previous = Environment.GetEnvironmentVariable("AIMONITOR_DISABLE_BROWSER_PROCESS");
        Environment.SetEnvironmentVariable("AIMONITOR_DISABLE_BROWSER_PROCESS", "true");
        try
        {
            StagedEditRecord record = new()
            {
                StagedRecordId = "staged-" + Guid.NewGuid().ToString("N")
            };

            DiffLaunchResult result = new BrowserStagedReviewLauncher().Launch(
                "http://localhost:5000",
                record);

            Assert.True(result.Launched);
            Assert.Equal("Browser", result.Tool);
            Assert.Equal(0, result.ProcessId);
            Assert.Equal(
                $"http://localhost:5000/review/staged/{record.StagedRecordId}",
                result.ToolPath);
            Assert.Contains("Browser process launch is disabled", result.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIMONITOR_DISABLE_BROWSER_PROCESS", previous);
        }
    }
}
