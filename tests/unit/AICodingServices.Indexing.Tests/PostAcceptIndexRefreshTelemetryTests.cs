namespace AICodingServices.Indexing.Tests;

public sealed class PostAcceptIndexRefreshTelemetryTests
{
    [Fact]
    public void ShouldLogPhase_returns_false_for_per_project_phase_rows()
    {
        bool shouldLog = PostAcceptIndexRefreshTelemetry.ShouldLogPhase(
            new Dictionary<string, string>
            {
                ["projectPath"] = @"C:\repo\src\Example\Example.csproj",
                ["phase"] = "index.sqlite.insert-symbols"
            });

        Assert.False(shouldLog);
    }

    [Fact]
    public void ShouldLogPhase_returns_true_for_solution_level_phase_rows()
    {
        bool shouldLog = PostAcceptIndexRefreshTelemetry.ShouldLogPhase(
            new Dictionary<string, string>
            {
                ["phase"] = "index.full.sqlite-save"
            });

        Assert.True(shouldLog);
    }
}
