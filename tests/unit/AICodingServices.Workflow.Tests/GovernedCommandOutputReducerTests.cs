using AICodingServices.Workflow;

namespace AICodingServices.Workflow.Tests;

public sealed class GovernedCommandOutputReducerTests
{
    [Fact]
    public void Build_output_reduction_keeps_counts_and_diagnostics_without_full_log()
    {
        GovernedCommandOutputReducer reducer = new();
        GovernedCommandRequest request = new("dotnet build Sample.csproj");
        GovernedCommandRawResult raw = new(
            """
              Sample -> C:\tmp\Sample.dll
            C:\src\Program.cs(10,5): error CS0103: The name 'Missing' does not exist in the current context [C:\src\Sample.csproj]
            Build FAILED.
            """,
            string.Empty,
            1,
            TimeSpan.FromMilliseconds(123));

        GovernedCommandReductionResult result = reducer.Reduce(request, raw);

        Assert.Equal(GovernedCommandKind.Build, result.Kind);
        Assert.Equal(GovernedCommandOutputMode.Diagnostics, result.OutputMode);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Total Projects Compiled:", result.VisibleOutput, StringComparison.Ordinal);
        Assert.Contains("Total Failed: 1", result.VisibleOutput, StringComparison.Ordinal);
        Assert.Contains("CS0103", result.VisibleOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Build FAILED.", result.VisibleOutput, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CS0103");
    }
}
