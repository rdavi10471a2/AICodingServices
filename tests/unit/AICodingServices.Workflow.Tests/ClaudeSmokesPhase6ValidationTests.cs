using AICodingServices.Core;
using AICodingServices.Workflow;

namespace AICodingServices.Workflow.Tests;

// ClaudeSmokes — Phase 6 CI gate (per-edit validation is FEEDBACK, not a gate), authored by Claude
// (review+test role; no production edits). LOCAL.
//
// Pins the scope-safety contract recalibrated this session: per-edit overlay validation provides structured
// diagnostics but must NOT block the edit, and must NOT apply C# validation to non-C# assets. The real safety gate
// stays the pre-merge full build. These fail if per-edit validation becomes a hard blocker (scope-creep) or stops
// surfacing diagnostics (orphaned).
public sealed class ClaudeSmokesPhase6ValidationTests
{
    [Fact]
    [Trait("Suite", "ClaudeSmokes")]
    public void ClaudeSmokes_semantic_error_edit_is_feedback_not_a_block()
    {
        (WorkflowEditService service, string watchedRoot, string programFilePath) = CreateFixture();
        EditSessionStatus refresh = service.Refresh(programFilePath);
        File.WriteAllText(refresh.WorkingFilePath, "namespace Example { internal static class Program { static int M() => 0; } }");

        // Inject a CS0103 (undefined name) — a SEMANTIC error.
        TextSpanResult span = service.FindTextSpan(programFilePath, "0");
        EditSessionStatus result = service.ReplaceSpan(
            programFilePath,
            span.StartLine,
            span.StartColumn,
            span.EndLine,
            span.EndColumn,
            "MissingThing",
            expectedOldText: "0");

        // Edit is NOT blocked by the semantic error (the overlay is feedback, not a gate).
        Assert.Equal(1, result.OperationCount);
        // ...but the structured overlay diagnostics ARE surfaced as feedback.
        Assert.NotNull(result.OverlayValidation);
        Assert.Equal("compiled-with-errors", result.OverlayValidation!.Status);
        Assert.Contains(result.OverlayValidation.Diagnostics, diagnostic => diagnostic.Id == "CS0103");
    }

    [Fact]
    [Trait("Suite", "ClaudeSmokes")]
    public void ClaudeSmokes_non_csharp_edit_is_not_csharp_validated()
    {
        (WorkflowEditService service, string watchedRoot, _) = CreateFixture();
        string jsonPath = Path.Combine(watchedRoot, "config.json");
        File.WriteAllText(jsonPath, "{ \"flag\": \"on\" }");

        EditSessionStatus refresh = service.Refresh(jsonPath);
        File.WriteAllText(refresh.WorkingFilePath, "{ \"flag\": \"on\" }");

        ReplaceTextResult result = service.ReplaceText(jsonPath, "on", "off", expectedMatches: 1);

        Assert.True(result.Changed);
        Assert.NotNull(result.OverlayValidation);
        Assert.Equal("skipped-non-csharp", result.OverlayValidation!.Status);
    }

    private static (WorkflowEditService Service, string WatchedRoot, string ProgramFilePath) CreateFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesClaudeSmokesP6", Guid.NewGuid().ToString("N"));
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string programFilePath = Path.Combine(watchedRoot, "Program.cs");
        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(programFilePath, "namespace Example { internal static class Program { } }");
        MonitorSettings settings = MonitorSettings.Create(Path.Combine(tempRoot, "Repo"), projectPath, Path.Combine(tempRoot, "Runtime"));
        return (new WorkflowEditService(settings), watchedRoot, programFilePath);
    }
}
