namespace AICodingServices.MSBuild;

public sealed record BuildProjectSummary(
    BuildValidationPhase Phase,
    BuildProjectCounts Counts);
