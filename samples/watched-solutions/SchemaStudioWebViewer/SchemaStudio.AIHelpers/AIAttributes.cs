using System;

namespace SchemaStudio.AIHelpers;

/// <summary>
/// Legacy status enum retained for compatibility with older watched projects.
/// New workflow code should generally use monitor ledgers instead of status-bearing source attributes.
/// </summary>
public enum AICommandStatus
{
    Pending,
    Completed,
    Verified,
    Rejected
}

/// <summary>
/// Legacy protection marker retained so older monitored projects continue to compile.
/// New workflow guidance should prefer explicit user instructions and monitor diffs instead of broad source-level locks.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
public sealed class DoNotRefactorAttribute : Attribute
{
    public string Reason { get; }
    public string Warning => "CRITICAL: Do not refactor without asking. DO NOT remove or relocate existing comments.";

    public DoNotRefactorAttribute(string reason)
    {
        Reason = reason;
    }
}

/// <summary>
/// Legacy instruction marker retained for compatibility with older watched projects.
/// Routine instructions should now live in prompts, ledgers, or component maps instead of stacked source attributes.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AIInstructionsAttribute : Attribute
{
    public string Command { get; }
    public AICommandStatus Status { get; set; }

    public AIInstructionsAttribute(string command, AICommandStatus status = AICommandStatus.Pending)
    {
        Command = command;
        Status = status;
    }
}

/// <summary>
/// Describes the durable purpose, responsibilities, and local gotchas for a source file.
/// Use this sparingly for files that are edited often, split across partials, or hard to understand from syntax alone.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class AIFileContextAttribute : Attribute
{
    public string FileName { get; }
    public string Purpose { get; }
    public string Responsibilities { get; set; } = string.Empty;
    public string Nuances { get; set; } = string.Empty;
    public string RelatedFiles { get; set; } = string.Empty;
    public string LastReviewed { get; set; } = string.Empty;

    public AIFileContextAttribute(string fileName, string purpose)
    {
        FileName = fileName;
        Purpose = purpose;
    }
}

/// <summary>
/// Tracks the source file's human-visible version for monitor-assisted edits.
/// For partial classes, each physical partial file may have its own file-level version.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class FileVersionAttribute : Attribute
{
    public string Version { get; }

    public FileVersionAttribute(string version)
    {
        Version = version;
    }
}

/// <summary>
/// Optional compact marker for unusual edits that need to remain visible in source.
/// Routine edit history belongs in the monitor-owned file ledger, not in stacked source attributes.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
public sealed class AIChangeAttribute : Attribute
{
    public string Version { get; }
    public string Summary { get; }
    public string Command => Summary;
    public AICommandStatus Status { get; set; }
    public string Timestamp { get; set; } = string.Empty;

    public AIChangeAttribute(string version, string summary)
        : this(version, summary, AICommandStatus.Pending)
    {
    }

    public AIChangeAttribute(string version, string summary, AICommandStatus status)
    {
        Version = version;
        Summary = summary;
        Status = status;
    }
}

/// <summary>
/// Legacy AI history marker retained for older source files.
/// Routine history should now go to the monitor ledger.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AIHistoryAttribute : Attribute
{
    public string Version { get; }
    public string ChangeLog { get; }
    public string Timestamp { get; }

    public AIHistoryAttribute(string version, string changeLog)
    {
        Version = version;
        ChangeLog = changeLog;
        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }
}

/// <summary>
/// Legacy human history marker retained for older source files.
/// Prefer Git and component maps for durable human history in new work.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class UserHistoryAttribute : Attribute
{
    public string Version { get; }
    public string ChangeLog { get; }
    public string Timestamp { get; }

    public UserHistoryAttribute(string version, string changeLog)
    {
        Version = version;
        ChangeLog = changeLog;
        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }
}
