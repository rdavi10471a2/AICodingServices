using System.Text;
using System.Text.RegularExpressions;

namespace AICodingServices.Workflow;

public sealed class GovernedCommandOutputReducer
{
    private static readonly Regex DiagnosticPattern = new(
        "^(?<file>.+?)\\((?<line>\\d+),(?<column>\\d+)\\): (?<severity>error|warning) (?<code>[A-Z]+\\d+): (?<message>.*?)(?: \\[(?<project>.+)\\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly GovernedCommandPolicyOptions options;

    public GovernedCommandOutputReducer()
        : this(new GovernedCommandPolicyOptions())
    {
    }

    public GovernedCommandOutputReducer(GovernedCommandPolicyOptions options)
    {
        this.options = options;
    }

    public GovernedCommandReductionResult Reduce(
        GovernedCommandRequest request,
        GovernedCommandRawResult rawResult,
        string? fullOutputArtifactPath = null,
        GovernedCommandOutputMode? requestedOutputMode = null)
    {
        GovernedCommandKind kind = Classify(request.Command);
        string combinedOutput = CombineOutput(rawResult.StandardOutput, rawResult.StandardError);
        List<string> lines = SplitLines(combinedOutput);
        List<GovernedCommandDiagnostic> diagnostics = ExtractDiagnostics(lines);
        List<GovernedCommandWarning> warnings = CreateWarnings(request, combinedOutput, fullOutputArtifactPath);
        GovernedCommandOutputMode outputMode = requestedOutputMode ?? ChooseOutputMode(kind, diagnostics.Count);
        string visibleOutput = BuildVisibleOutput(kind, outputMode, lines, diagnostics);
        bool truncated = visibleOutput.Length < combinedOutput.Length;

        if (truncated)
        {
            warnings.Add(new GovernedCommandWarning(
                GovernedCommandWarningCode.OutputTruncated,
                "Command output was reduced before entering model context."));
        }

        if (truncated && string.IsNullOrWhiteSpace(fullOutputArtifactPath))
        {
            warnings.Add(new GovernedCommandWarning(
                GovernedCommandWarningCode.FullOutputArtifactMissing,
                "Reduced command output has no full-output artifact path."));
        }

        return new GovernedCommandReductionResult(
            kind,
            outputMode,
            rawResult.ExitCode,
            (long)rawResult.Duration.TotalMilliseconds,
            combinedOutput.Length,
            visibleOutput.Length,
            lines.Count,
            truncated,
            visibleOutput,
            fullOutputArtifactPath,
            diagnostics,
            warnings);
    }

    public static GovernedCommandKind Classify(string command)
    {
        string trimmed = command.Trim();
        string lower = trimmed.ToLowerInvariant();

        if (ContainsRuntimeWorkingPath(lower))
        {
            return GovernedCommandKind.RuntimeDebug;
        }

        if (lower.StartsWith("dotnet build", StringComparison.Ordinal)
            || lower.Contains(" dotnet build ", StringComparison.Ordinal))
        {
            return GovernedCommandKind.Build;
        }

        if (lower.StartsWith("dotnet test", StringComparison.Ordinal)
            || lower.Contains(" dotnet test ", StringComparison.Ordinal))
        {
            return GovernedCommandKind.Test;
        }

        if (lower.StartsWith("git ", StringComparison.Ordinal))
        {
            return GovernedCommandKind.Git;
        }

        if (lower.Contains("get-nettcpconnection", StringComparison.Ordinal)
            || lower.Contains("get-ciminstance", StringComparison.Ordinal)
            || lower.Contains("get-process", StringComparison.Ordinal)
            || lower.StartsWith("tasklist", StringComparison.Ordinal))
        {
            return GovernedCommandKind.ProcessStatus;
        }

        if (lower.Contains("select-string", StringComparison.Ordinal)
            || lower.StartsWith("rg ", StringComparison.Ordinal)
            || lower.Contains(" rg ", StringComparison.Ordinal))
        {
            return GovernedCommandKind.Search;
        }

        if (lower.Contains("get-content", StringComparison.Ordinal)
            || lower.StartsWith("type ", StringComparison.Ordinal)
            || lower.StartsWith("cat ", StringComparison.Ordinal))
        {
            return GovernedCommandKind.FileRead;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return GovernedCommandKind.Unknown;
        }

        return GovernedCommandKind.Other;
    }

    private static GovernedCommandOutputMode ChooseOutputMode(GovernedCommandKind kind, int diagnosticCount)
    {
        if (diagnosticCount > 0 || kind == GovernedCommandKind.Build || kind == GovernedCommandKind.Test)
        {
            return GovernedCommandOutputMode.Diagnostics;
        }

        if (kind == GovernedCommandKind.Search)
        {
            return GovernedCommandOutputMode.Matches;
        }

        return GovernedCommandOutputMode.Summary;
    }

    private static string CombineOutput(string standardOutput, string standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return standardOutput;
        }

        if (string.IsNullOrEmpty(standardOutput))
        {
            return standardError;
        }

        return standardOutput + Environment.NewLine + standardError;
    }

    private static List<string> SplitLines(string output)
    {
        return output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
    }

    private static List<GovernedCommandDiagnostic> ExtractDiagnostics(IEnumerable<string> lines)
    {
        List<GovernedCommandDiagnostic> diagnostics = [];
        foreach (string line in lines)
        {
            Match match = DiagnosticPattern.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            diagnostics.Add(new GovernedCommandDiagnostic(
                match.Groups["file"].Value,
                int.Parse(match.Groups["line"].Value),
                int.Parse(match.Groups["column"].Value),
                match.Groups["severity"].Value.ToLowerInvariant(),
                match.Groups["code"].Value,
                match.Groups["message"].Value.Trim(),
                match.Groups["project"].Success ? match.Groups["project"].Value : null));
        }

        return diagnostics;
    }

    private static List<GovernedCommandWarning> CreateWarnings(
        GovernedCommandRequest request,
        string combinedOutput,
        string? fullOutputArtifactPath)
    {
        List<GovernedCommandWarning> warnings = [];
        if (ContainsRuntimeWorkingPath(request.Command)
            || ContainsRuntimeWorkingPath(request.WorkingDirectory ?? string.Empty))
        {
            warnings.Add(new GovernedCommandWarning(
                GovernedCommandWarningCode.RuntimeWorkingRead,
                "Command targets the monitor-owned Working tree; use MCP workflow tools unless debugging workflow internals."));
        }

        return warnings;
    }

    private string BuildVisibleOutput(
        GovernedCommandKind kind,
        GovernedCommandOutputMode outputMode,
        IReadOnlyList<string> lines,
        IReadOnlyList<GovernedCommandDiagnostic> diagnostics)
    {
        if (outputMode == GovernedCommandOutputMode.Full)
        {
            return string.Join(Environment.NewLine, lines);
        }

        if (outputMode == GovernedCommandOutputMode.Diagnostics && diagnostics.Count > 0)
        {
            return BuildDiagnosticSummary(diagnostics);
        }

        if (outputMode == GovernedCommandOutputMode.Diagnostics
            && (kind == GovernedCommandKind.Build || kind == GovernedCommandKind.Test))
        {
            return BuildBuildProgressSummary(lines);
        }

        if (outputMode == GovernedCommandOutputMode.Matches)
        {
            return BoundText(string.Join(Environment.NewLine, lines.Take(options.MaxSearchMatches)));
        }

        return BuildBoundedContext(lines);
    }

    private string BuildDiagnosticSummary(IReadOnlyList<GovernedCommandDiagnostic> diagnostics)
    {
        StringBuilder builder = new();
        foreach (GovernedCommandDiagnostic diagnostic in diagnostics)
        {
            builder
                .Append(diagnostic.Severity)
                .Append(' ')
                .Append(diagnostic.Code)
                .Append(": ")
                .Append(diagnostic.Message);

            if (!string.IsNullOrWhiteSpace(diagnostic.FilePath))
            {
                builder
                    .Append(" (")
                    .Append(diagnostic.FilePath)
                    .Append(':')
                    .Append(diagnostic.Line)
                    .Append(')');
            }

            builder.AppendLine();
        }

        return BoundText(builder.ToString().TrimEnd());
    }

    private string BuildBuildProgressSummary(IReadOnlyList<string> lines)
    {
        string[] projectOutputLines = lines
            .Where(IsBuildProjectOutputLine)
            .TakeLast(options.ContextLineCount)
            .ToArray();
        string[] summaryLines = lines
            .Where(IsBuildSummaryLine)
            .TakeLast(6)
            .ToArray();
        if (projectOutputLines.Length == 0 && summaryLines.Length == 0)
        {
            return BuildBoundedContext(lines);
        }

        StringBuilder builder = new();
        if (projectOutputLines.Length > 0)
        {
            builder.AppendLine("Last successful project outputs:");
            foreach (string line in projectOutputLines)
            {
                builder.AppendLine(line.TrimEnd());
            }
        }

        if (summaryLines.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("Build summary:");
            foreach (string line in summaryLines)
            {
                builder.AppendLine(line.TrimEnd());
            }
        }

        return BoundText(builder.ToString().TrimEnd());
    }

    private static bool IsBuildProjectOutputLine(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Contains(" -> ", StringComparison.Ordinal)
            && (trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBuildSummaryLine(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Equals("Build succeeded.", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Build FAILED.", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Warning(s)", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Error(s)", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Time Elapsed", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildBoundedContext(IReadOnlyList<string> lines)
    {
        if (lines.Count <= options.ContextLineCount * 2)
        {
            return BoundText(string.Join(Environment.NewLine, lines));
        }

        IEnumerable<string> head = lines.Take(options.ContextLineCount);
        IEnumerable<string> tail = lines.Skip(Math.Max(options.ContextLineCount, lines.Count - options.ContextLineCount));
        string text = string.Join(Environment.NewLine, head)
            + Environment.NewLine
            + $"... {lines.Count - (options.ContextLineCount * 2)} line(s) omitted ..."
            + Environment.NewLine
            + string.Join(Environment.NewLine, tail);

        return BoundText(text);
    }

    private string BoundText(string text)
    {
        if (text.Length <= options.MaxVisibleCharacters)
        {
            return text;
        }

        int markerLength = Environment.NewLine.Length + "... output truncated ...".Length;
        int keep = Math.Max(0, options.MaxVisibleCharacters - markerLength);
        return text[..keep] + Environment.NewLine + "... output truncated ...";
    }

    private static bool ContainsRuntimeWorkingPath(string value)
    {
        string lower = value.ToLowerInvariant().Replace('/', '\\');
        return lower.Contains("runtime\\watched-solutions", StringComparison.Ordinal)
            && lower.Contains("\\working", StringComparison.Ordinal);
    }
}
