# SK-Focused Rewrite Proposal

**Purpose:** Semantic Kernel as the agent-facing contract, enforcing skill card policies and governed commands.

**Current Architecture:**
```
Agent → MCP Server (all tools visible) → Agent picks
```

**Proposed Architecture:**
```
Agent → SK Kernel (valid tools only) → MCP Server → Source
```

---

## Project Structure

```
src/
├── AICodingServices.SKGovernor/           # NEW
│   ├── AICodingServices.SKGovernor.csproj
│   ├── KernelBuilder.cs
│   ├── IntentClassifier.cs
│   ├── PolicyEnforcer.cs
│   ├── Tools/
│   │   ├── EditToolsPlugin.cs
│   │   ├── DiscoveryToolsPlugin.cs
│   │   ├── BuildToolsPlugin.cs
│   │   └── SessionToolsPlugin.cs
│   ├── Guards/
│   │   ├── ShellCommandGuard.cs
│   │   ├── WorkingPathGuard.cs
│   │   └── PreciseEditGuard.cs
│   └── Governed/
│       ├── GovernedBuildExecutor.cs
│       └── BuildMode.cs
```

---

## Core Files

### KernelBuilder.cs

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;

namespace AICodingServices.SKGovernor;

public sealed class SKGovernorKernelBuilder
{
    private readonly MonitorSettings settings;
    private readonly SolutionIndexQueryService queryService;
    private readonly WorkflowEditService workflowService;
    private readonly IMonitorLogger logger;

    public SKGovernorKernelBuilder(
        MonitorSettings settings,
        SolutionIndexQueryService queryService,
        WorkflowEditService workflowService,
        IMonitorLogger logger)
    {
        this.settings = settings;
        this.queryService = queryService;
        this.workflowService = workflowService;
        this.logger = logger;
    }

    public Kernel Build()
    {
        Kernel kernel = Kernel.CreateBuilder()
            .Configure(config =>
            {
                config.AddService(settings);
                config.AddService(queryService);
                config.AddService(workflowService);
                config.AddService(logger);
            })
            .Build();

        // Add plugins - order matters for policy enforcement
        kernel.Plugins.AddFromObject(new ShellCommandGuard(logger));
        kernel.Plugins.AddFromObject(new WorkingPathGuard(logger));
        kernel.Plugins.AddFromObject(new PreciseEditGuard());
        kernel.Plugins.AddFromObject(new EditToolsPlugin(settings, workflowService, logger));
        kernel.Plugins.AddFromObject(new DiscoveryToolsPlugin(settings, queryService, logger));
        kernel.Plugins.AddFromObject(new BuildToolsPlugin(settings, logger));
        kernel.Plugins.AddFromObject(new SessionToolsPlugin(settings, workflowService, logger));

        return kernel;
    }
}
```

---

### IntentClassifier.cs

```csharp
using System.Text.RegularExpressions;

namespace AICodingServices.SKGovernor;

public sealed class IntentClassifier
{
    private static readonly Regex EditMemberPattern = new(
        @"replace.*method|change.*property|modify.*field|edit.*constructor",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex AddMemberPattern = new(
        @"add.*method|add.*property|add.*field|add.*constructor|new.*member",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex RemoveMemberPattern = new(
        @"remove.*method|delete.*property|remove.*member",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex TextEditPattern = new(
        @"replace.*text|change.*string|update.*config|edit.*json|edit.*css|edit.*razor.*markup",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex NewFilePattern = new(
        @"create.*file|new.*file|add.*file",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex BuildPattern = new(
        @"build|compile|test|run|restore",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex DiscoverPattern = new(
        @"find.*symbol|search.*method|where.*is|look.*up|find.*references",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IntentClass Classify(string agentIntent)
    {
        if (EditMemberPattern.IsMatch(agentIntent))
            return IntentClass.EditMember;
        
        if (AddMemberPattern.IsMatch(agentIntent))
            return IntentClass.AddMember;
        
        if (RemoveMemberPattern.IsMatch(agentIntent))
            return IntentClass.RemoveMember;
        
        if (TextEditPattern.IsMatch(agentIntent))
            return IntentClass.TextEdit;
        
        if (NewFilePattern.IsMatch(agentIntent))
            return IntentClass.NewFile;
        
        if (BuildPattern.IsMatch(agentIntent))
            return IntentClass.Build;
        
        if (DiscoverPattern.IsMatch(agentIntent))
            return IntentClass.Discover;
        
        return IntentClass.Unknown;
    }

    public string[] GetValidTools(IntentClass intent, string? filePath = null)
    {
        return intent switch
        {
            IntentClass.EditMember => 
                ["submit_symbol"],
            
            IntentClass.AddMember => ClassifyMemberType(filePath) switch
            {
                MemberType.Method => ["add_method"],
                MemberType.Property => ["add_property"],
                MemberType.Field => ["add_field"],
                MemberType.Constructor => ["add_constructor"],
                _ => ["add_method"]
            },
            
            IntentClass.RemoveMember => 
                ["remove_symbol"],
            
            IntentClass.TextEdit => 
                ["replace_text_in_file", "replace_span_in_file"],
            
            IntentClass.NewFile => ClassifyNewFileType(filePath) switch
            {
                NewFileType.Razor => ["submit_file"], // Requires 3 files
                NewFileType.CSharp => ["submit_file"],
                NewFileType.Config => ["submit_file"],
                _ => ["submit_file"]
            },
            
            IntentClass.Build => 
                ["governed_build"],
            
            IntentClass.Discover => 
                ["find_indexed_symbols", "find_indexed_references", "get_symbol", "get_source_map"],
            
            _ => throw new InvalidOperationException($"Unknown intent: {intent}")
        };
    }

    private static MemberType ClassifyMemberType(string? path)
    {
        if (string.IsNullOrEmpty(path)) return MemberType.Method;
        // Would use source analysis here
        return MemberType.Method;
    }

    private static NewFileType ClassifyNewFileType(string? path)
    {
        if (string.IsNullOrEmpty(path)) return NewFileType.CSharp;
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext switch
        {
            ".razor" => NewFileType.Razor,
            ".cshtml" => NewFileType.Razor,
            ".json" or ".config" or ".csproj" => NewFileType.Config,
            _ => NewFileType.CSharp
        };
    }
}

public enum IntentClass
{
    Unknown,
    EditMember,
    AddMember,
    RemoveMember,
    TextEdit,
    NewFile,
    Build,
    Discover
}

public enum MemberType { Method, Property, Field, Constructor }

public enum NewFileType { CSharp, Razor, Config }
```

---

### PolicyEnforcer.cs

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Logging;

namespace AICodingServices.SKGovernor;

public sealed class PolicyEnforcer
{
    private readonly IMonitorLogger logger;
    private readonly IntentClassifier classifier;

    public PolicyEnforcer(IMonitorLogger logger, IntentClassifier classifier)
    {
        this.logger = logger;
        this.classifier = classifier;
    }

    public PolicyDecision Enforce(string agentIntent, string requestedTool, string? filePath = null)
    {
        IntentClass intent = classifier.Classify(agentIntent);
        
        if (intent == IntentClass.Unknown)
        {
            return new PolicyDecision
            {
                Allowed = false,
                Reason = "Could not classify intent. Reframe your request."
            };
        }

        string[] validTools = classifier.GetValidTools(intent, filePath);

        if (!validTools.Contains(requestedTool, StringComparer.OrdinalIgnoreCase))
        {
            logger.Write(
                MonitorLogLevel.Warning,
                "SKGovernor.PolicyEnforcer",
                "policy.denied",
                "Tool not valid for intent.",
                new Dictionary<string, string>
                {
                    ["intent"] = intent.ToString(),
                    ["requestedTool"] = requestedTool,
                    ["validTools"] = string.Join(", ", validTools)
                });

            return new PolicyDecision
            {
                Allowed = false,
                Reason = $"'{requestedTool}' is not valid for '{intent}'. Use: {string.Join(" or ", validTools)}",
                Suggestion = $"Try '{validTools[0]}' instead."
            };
        }

        // Additional guards
        if (requestedTool is "replace_text_in_file" or "replace_span_in_file")
        {
            return EnforceTextEditPolicy(filePath);
        }

        return new PolicyDecision { Allowed = true };
    }

    private PolicyDecision EnforceTextEditPolicy(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return new PolicyDecision { Allowed = true };
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        // Text edits allowed for these file types
        string[] textEditAllowed = [".razor", ".cshtml", ".json", ".config", ".css", ".xml", ".md"];
        
        if (textEditAllowed.Contains(extension))
        {
            return new PolicyDecision { Allowed = true };
        }

        // For .cs files, reject unless it's clearly a config/style file
        if (extension == ".cs")
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.Contains("AssemblyInfo", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("Version", StringComparison.OrdinalIgnoreCase))
            {
                return new PolicyDecision { Allowed = true };
            }

            return new PolicyDecision
            {
                Allowed = false,
                Reason = ".cs files should use precise symbol tools (submit_symbol, add_method, etc.)",
                Suggestion = "Use submit_symbol to edit member bodies, add_method to add new methods."
            };
        }

        return new PolicyDecision { Allowed = true };
    }
}

public sealed record PolicyDecision
{
    public required bool Allowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? Suggestion { get; init; }
}
```

---

### BuildToolsPlugin.cs

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Logging;
using AICodingServices.Workflow;

namespace AICodingServices.SKGovernor;

public sealed class BuildToolsPlugin
{
    private readonly MonitorSettings settings;
    private readonly IMonitorLogger logger;

    public BuildToolsPlugin(MonitorSettings settings, IMonitorLogger logger)
    {
        this.settings = settings;
        this.logger = logger;
    }

    [KernelFunction("governed_build")]
    [Description(
        """
        Run dotnet build with governed output. Only builds are allowed via shell.
        
        Modes:
        - normal: Build + test
        - prototype: Build only (skip tests during active editing)
        - verify: Build + test + full diagnostics
        
        Returns compact summary. Full output logged to runtime/tool-logs/.
        """)]
    public async Task<GovernedBuildResult> GovernedBuild(
        [Description("Build mode: normal, prototype, or verify")] string mode = "normal",
        [Description("Optional specific project path")] string? projectPath = null,
        [Description("Optional explicit arguments")] string? extraArgs = null)
    {
        BuildMode buildMode = mode.ToLowerInvariant() switch
        {
            "prototype" => BuildMode.Prototype,
            "verify" => BuildMode.Verify,
            _ => BuildMode.Normal
        };

        GovernedBuildExecutor executor = new(settings, logger);
        GovernedBuildResult result = await executor.ExecuteAsync(
            projectPath,
            buildMode,
            extraArgs);

        logger.Write(
            MonitorLogLevel.Information,
            "SKGovernor.BuildTools",
            "governed.build.completed",
            result.Message,
            new Dictionary<string, string>
            {
                ["mode"] = mode,
                ["status"] = result.Status,
                ["exitCode"] = result.ExitCode.ToString(),
                ["reduction"] = $"{result.ReductionPercent:F1}%",
                ["fullArtifact"] = result.FullArtifactPath ?? "none"
            });

        return result;
    }
}

public sealed class GovernedBuildExecutor
{
    private readonly MonitorSettings settings;
    private readonly IMonitorLogger logger;
    private readonly GovernedCommandOutputReducer reducer;
    private readonly GovernedCommandArtifactWriter artifactWriter;

    public GovernedBuildExecutor(MonitorSettings settings, IMonitorLogger logger)
    {
        this.settings = settings;
        this.logger = logger;
        this.reducer = new GovernedCommandOutputReducer();
        this.artifactWriter = new GovernedCommandArtifactWriter(settings.RuntimeRoot);
    }

    public async Task<GovernedBuildResult> ExecuteAsync(
        string? projectPath,
        BuildMode mode,
        string? extraArgs,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["build"];
        
        if (!string.IsNullOrEmpty(projectPath))
        {
            arguments.Add(projectPath);
        }

        arguments.Add("--nologo");
        arguments.Add("-v:minimal");
        
        if (mode == BuildMode.Prototype)
        {
            arguments.Add("--no-restore"); // Assume already restored
        }
        else if (mode == BuildMode.Normal || mode == BuildMode.Verify)
        {
            // Full restore + build
            if (mode == BuildMode.Verify)
            {
                arguments.Add("--no-restore");
                // Add test arguments if needed
            }
        }

        if (!string.IsNullOrEmpty(extraArgs))
        {
            arguments.AddRange(extraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        DateTimeOffset start = DateTimeOffset.UtcNow;
        
        ProcessResult processResult = await RunProcessAsync(
            "dotnet",
            arguments,
            settings.WatchedProjectFolder,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        TimeSpan duration = DateTimeOffset.UtcNow - start;
        string fullArtifactPath = artifactWriter.WriteArtifact(
            new GovernedCommandRequest(
                "dotnet " + string.Join(" ", arguments),
                settings.WatchedProjectFolder),
            new GovernedCommandRawResult(
                processResult.StandardOutput,
                processResult.StandardError,
                processResult.ExitCode,
                duration),
            GovernedCommandKind.Build);

        GovernedCommandReductionResult reduction = reducer.Reduce(
            new GovernedCommandRequest(
                "dotnet " + string.Join(" ", arguments),
                settings.WatchedProjectFolder),
            new GovernedCommandRawResult(
                processResult.StandardOutput,
                processResult.StandardError,
                processResult.ExitCode,
                duration),
            fullArtifactPath);

        int rawChars = reduction.RawOutputCharacters;
        int visibleChars = reduction.VisibleOutputCharacters;
        double reductionPercent = rawChars > 0 
            ? (1.0 - (double)visibleChars / rawChars) * 100 
            : 0;

        return new GovernedBuildResult
        {
            Status = processResult.ExitCode == 0 ? "passed" : "failed",
            Message = reduction.VisibleOutput,
            ExitCode = processResult.ExitCode,
            DurationMs = (long)duration.TotalMilliseconds,
            RawBytes = rawChars,
            VisibleBytes = visibleChars,
            ReductionPercent = reductionPercent,
            FullArtifactPath = fullArtifactPath,
            Diagnostics = reduction.Diagnostics.ToList(),
            Mode = mode.ToString()
        };
    }

    private static async Task<GovernedCommandRawResult> RunProcessAsync(
        string fileName,
        List<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo psi = new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process? process = Process.Start(psi);
        if (process == null)
        {
            return new GovernedCommandRawResult(
                string.Empty,
                "Failed to start process",
                -1,
                TimeSpan.Zero);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        bool completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
        
        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new GovernedCommandRawResult(
                await stdoutTask,
                "Process timed out",
                -1,
                timeout);
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        
        return new GovernedCommandRawResult(
            await stdoutTask,
            await stderrTask,
            process.ExitCode,
            TimeSpan.FromMilliseconds(
                (DateTimeOffset.UtcNow - DateTimeOffset.UtcNow).TotalMilliseconds));
    }
}

public enum BuildMode { Normal, Prototype, Verify }

public sealed record GovernedBuildResult
{
    public required string Status { get; init; }
    public required string Message { get; init; }
    public required int ExitCode { get; init; }
    public required long DurationMs { get; init; }
    public required int RawBytes { get; init; }
    public required int VisibleBytes { get; init; }
    public required double ReductionPercent { get; init; }
    public string? FullArtifactPath { get; init; }
    public List<GovernedCommandDiagnostic> Diagnostics { get; init; } = [];
    public string Mode { get; init; } = "normal";
}
```

---

### EditToolsPlugin.cs

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Logging;
using AICodingServices.Workflow;

namespace AICodingServices.SKGovernor;

public sealed class EditToolsPlugin
{
    private readonly MonitorSettings settings;
    private readonly WorkflowEditService workflowService;
    private readonly IMonitorLogger logger;

    public EditToolsPlugin(
        MonitorSettings settings,
        WorkflowEditService workflowService,
        IMonitorLogger logger)
    {
        this.settings = settings;
        this.workflowService = workflowService;
        this.logger = logger;
    }

    [KernelFunction("submit_symbol")]
    [Description("Replace a method, property, field, or entire type body. Preferred for .cs file edits.")]
    public SymbolEditResult SubmitSymbol(
        [Description("Stable symbol key from get_symbol or get_source_map")] string stableSymbolKey,
        [Description("New symbol content (full declaration)")] string newSymbolContent,
        [Description("Optional session id for planned edits")] string? sessionId = null)
    {
        // Implementation wraps WorkflowEditService.SubmitSymbol
        // Returns compact result
        // Logs to runtime/working/
        throw new NotImplementedException("Implementation in WorkflowEditService");
    }

    [KernelFunction("add_method")]
    [Description("Add a new method to an existing type.")]
    public SymbolEditResult AddMethod(
        [Description("Stable symbol key of the owning type")] string owningTypeKey,
        [Description("New method declaration")] string methodDeclaration,
        [Description("Optional session id")] string? sessionId = null)
    {
        throw new NotImplementedException();
    }

    [KernelFunction("remove_symbol")]
    [Description("Remove a member from a type.")]
    public SymbolEditResult RemoveSymbol(
        [Description("Stable symbol key of member to remove")] string stableSymbolKey,
        [Description("Optional session id")] string? sessionId = null)
    {
        throw new NotImplementedException();
    }

    [KernelFunction("stage_for_review")]
    [Description("Stage Working candidate for WinMerge/browser review.")]
    public StagedEditResult StageForReview(
        [Description("Source file path")] string sourceFilePath,
        [Description("Optional session id")] string? sessionId = null)
    {
        throw new NotImplementedException();
    }

    [KernelFunction("replace_text_in_file")]
    [Description(
        """
        Replace exact text in Razor, markup, CSS, JSON, config, or other text files.
        NOT for .cs files - use submit_symbol instead.
        """)]
    public TextEditResult ReplaceText(
        [Description("File path")] string path,
        [Description("Text to replace")] string oldText,
        [Description("Replacement text")] string newText,
        [Description("Optional session id")] string? sessionId = null)
    {
        // Reject .cs files here for safety
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".cs")
        {
            throw new InvalidOperationException(
                "Use submit_symbol for .cs file edits, not replace_text_in_file.");
        }

        throw new NotImplementedException();
    }
}

public sealed record SymbolEditResult
{
    public required bool Success { get; init; }
    public string? WorkingFilePath { get; init; }
    public string? StagedRecordId { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record TextEditResult
{
    public required bool Success { get; init; }
    public string? WorkingFilePath { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record StagedEditResult
{
    public required string StagedRecordId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
```

---

### DiscoveryToolsPlugin.cs

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Logging;
using AICodingServices.Data;

namespace AICodingServices.SKGovernor;

public sealed class DiscoveryToolsPlugin
{
    private readonly MonitorSettings settings;
    private readonly SolutionIndexQueryService queryService;
    private readonly IMonitorLogger logger;

    public DiscoveryToolsPlugin(
        MonitorSettings settings,
        SolutionIndexQueryService queryService,
        IMonitorLogger logger)
    {
        this.settings = settings;
        this.queryService = queryService;
        this.logger = logger;
    }

    [KernelFunction("find_indexed_symbols")]
    [Description("Find symbols in the solution index. Preferred over shell grep.")]
    public IndexedSymbolSearchResult FindSymbols(
        [Description("Search query (symbol name or pattern)")] string query,
        [Description("Optional namespace filter")] string? namespaceFilter = null,
        [Description("Symbol kind filter: method, property, field, class, struct, etc.")] string? kindFilter = null)
    {
        // Implementation uses queryService
        throw new NotImplementedException();
    }

    [KernelFunction("find_indexed_references")]
    [Description("Find all references to a symbol. Preferred over shell grep.")]
    public IndexedReferenceResult FindReferences(
        [Description("Stable symbol key")] string stableSymbolKey)
    {
        throw new NotImplementedException();
    }

    [KernelFunction("get_symbol")]
    [Description("Get symbol details including full declaration. Required before submit_symbol.")]
    public SymbolDetailResult GetSymbol(
        [Description("File path")] string filePath,
        [Description("Symbol selector (from get_source_map)")] string symbolSelector)
    {
        throw new NotImplementedException();
    }

    [KernelFunction("get_source_map")]
    [Description("Get file structure with symbol selectors. Use before editing.")]
    public SourceMapResult GetSourceMap(
        [Description("File path")] string filePath,
        [Description("Mode: navigation, selector, detail, or full")] string mode = "navigation")
    {
        throw new NotImplementedException();
    }
}

public sealed record IndexedSymbolSearchResult
{
    public required int TotalCount { get; init; }
    public List<IndexedSymbolRow> Symbols { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}

public sealed record IndexedReferenceResult
{
    public required string TargetKey { get; init; }
    public required string TargetName { get; init; }
    public required int ReferenceCount { get; init; }
    public List<AICodingServicesIndexedReferenceResult> References { get; init; } = [];
}

public sealed record SymbolDetailResult
{
    public required string StableSymbolKey { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Declaration { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
}

public sealed record SourceMapResult
{
    public required string FilePath { get; init; }
    public required List<SourceMapSymbol> Symbols { get; init; }
    public string FileHash { get; init; } = string.Empty;
}

public sealed record SourceMapSymbol
{
    public required string StableSymbolKey { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Signature { get; init; }
    public int Line { get; init; }
}
```

---

## Usage Flow

### Agent Request → SK Enforcement

```
1. Agent: "I want to replace the SubmitSymbol method body"
2. SK IntentClassifier: intent = EditMember
3. SK valid tools: ["submit_symbol"]
4. Agent requests: replace_text_in_file
5. SK PolicyEnforcer: DENIED - replace_text not valid for EditMember
6. SK returns: "Use submit_symbol instead"
7. Agent: uses submit_symbol ✅
```

### Build Flow

```
1. Agent: "build the project"
2. SK routes to governed_build
3. GovernedBuildExecutor: runs dotnet build
4. Output captured + reduced
5. Agent gets: 
   {
     "status": "passed",
     "message": "Total: 4, Succeeded: 4, Failed: 0",
     "reduction": "99.2%"
   }
6. Full output in runtime/tool-logs/
```

---

## What This Enables

| Feature | Current | With SK |
|---------|---------|---------|
| Tool selection | Agent picks | SK validates |
| Build output | Raw stdout | Compact summary |
| .cs edits | replace_text allowed | submit_symbol required |
| Shell commands | Any allowed | Build only |
| Working path reads | Allowed | Warning logged |
| Session continuity | Manual | Kernel memory |

---

## Next Steps

1. Create `AICodingServices.SKGovernor` project
2. Implement core files above
3. Wire into MCP server as SK bridge
4. Test with Codex agent
5. Measure token savings
