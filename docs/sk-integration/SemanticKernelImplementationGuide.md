# Semantic Kernel Implementation Guide for AICodingServices

**Branch:** `feature/semantic-kernel-implementation-guide`
**Created:** 2026-06-13
**Status:** Design Document (not production code)

---

## Executive Summary

This document provides a comprehensive guide for integrating Semantic Kernel (SK) into AICodingServices. The existing MCP server already demonstrates strong workflow orchestration patterns; SK can extend this by providing:

1. **Goal-oriented planning** for complex multi-file refactoring
2. **Plugin abstraction** for broader AI client support
3. **Memory integration** for session persistence and context
4. **Multi-model routing** for task-appropriate LLM selection

---

## Architecture Overview

### Current State

```
Claude/Codex
    │
    ▼
MCP Bridge ──────► MCP Server (AICodingServicesTools)
                           │
                           ▼
              ┌────────────────────────────┐
              │    Core Services           │
              │  • WorkflowEditService     │
              │  • SolutionIndexService    │
              │  • RoslynEditService       │
              │  • PreMergeValidation      │
              └────────────────────────────┘
```

### Proposed SK Integration

```
AI Client (Claude/GPT-4/Ollama)
    │
    ▼
┌─────────────────────────────────────────────┐
│           Semantic Kernel                    │
│  ┌────────────────────────────────────────┐  │
│  │         Kernel Plugins                  │  │
│  │  • WorkflowPlugin                       │  │
│  │  • SolutionIndexPlugin                 │  │
│  │  • ValidationPlugin                    │  │
│  │  • RoslynEditPlugin                    │  │
│  └────────────────────────────────────────┘  │
│                                             │
│  ┌────────────────────────────────────────┐  │
│  │         Planners                        │  │
│  │  • SequentialPlanner (simple)           │  │
│  │  • StepwisePlanner (complex)            │  │
│  └────────────────────────────────────────┘  │
│                                             │
│  ┌────────────────────────────────────────┐  │
│  │         Memory                          │  │
│  │  • SessionMemory                        │  │
│  │  • SemanticMemory                       │  │
│  └────────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
                           │
                           ▼
              ┌────────────────────────────┐
              │    Core Services           │
              │  (unchanged)               │
              └────────────────────────────┘
```

---

## Recommended Project Structure

### New Project: `AICodingServices.SKPlugins`

```
src/
├── AICodingServices.SKPlugins/
│   ├── AICodingServices.SKPlugins.csproj
│   ├── Plugins/
│   │   ├── WorkflowEditPlugin.cs
│   │   ├── SolutionIndexPlugin.cs
│   │   ├── RoslynEditPlugin.cs
│   │   ├── ValidationPlugin.cs
│   │   └── SessionManagementPlugin.cs
│   ├── Planners/
│   │   ├── EditWorkflowPlanner.cs
│   │   └── RefactorPlanner.cs
│   ├── Memory/
│   │   ├── EditSessionMemoryStore.cs
│   │   └── SemanticCodeMemory.cs
│   ├── Orchestration/
│   │   ├── AICodingServicesKernelBuilder.cs
│   │   └── MultiModelRouter.cs
│   └── Extensions/
│       └── KernelServiceCollectionExtensions.cs
```

### NuGet Dependencies

```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.24.0" />
<PackageReference Include="Microsoft.SemanticKernel.Plugins.Core" Version="1.24.0-preview" />
<PackageReference Include="Microsoft.SemanticKernel.Planners.Handlebars" Version="1.24.0-preview" />
```

---

## Plugin Implementations

### 1. WorkflowEditPlugin

Wraps `WorkflowEditService` for SK.

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Workflow;

namespace AICodingServices.SKPlugins.Plugins;

public sealed class WorkflowEditPlugin
{
    private readonly MonitorSettings _settings;
    private readonly WorkflowEditService _workflowService;

    public WorkflowEditPlugin(MonitorSettings settings)
    {
        _settings = settings;
        _workflowService = new WorkflowEditService(settings);
    }

    [SKFunction("Refresh a watched source file into the monitor-owned Working folder")]
    [SKParameterName("sourceFilePath")]
    public EditSessionStatus Refresh(string sourceFilePath)
    {
        return _workflowService.Refresh(ResolvePath(sourceFilePath));
    }

    [SKFunction("Replace exact text in the Working candidate")]
    [SKParameterName("path")]
    [SKParameterName("oldText")]
    [SKParameterName("newText")]
    public ReplaceTextResult ReplaceText(
        string path,
        string oldText,
        string newText,
        [SKParameterName("sessionId")] string? sessionId = null)
    {
        return _workflowService.ReplaceText(
            ResolvePath(path),
            oldText,
            newText,
            expectedMatchCount: 1,
            expectedFileHash: null,
            occurrenceIndex: null,
            manifestJson: null,
            validateSyntax: true);
    }

    [SKFunction("Stage the Working candidate for review")]
    [SKParameterName("path")]
    [SKParameterName("sessionId")]
    public StagedEditRecord Stage(
        string path,
        string? sessionId = null,
        string? ledgerSummary = null)
    {
        return _workflowService.Stage(ResolvePath(path), ledgerSummary, sessionId);
    }

    [SKFunction("Get the current edit session status")]
    [SKParameterName("sourceFilePath")]
    public EditSessionStatus GetStatus(string sourceFilePath)
    {
        return _workflowService.GetStatus(ResolvePath(sourceFilePath));
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(_settings.WatchedProjectFolder, path));
    }
}
```

### 2. SolutionIndexPlugin

Wraps `SolutionIndexQueryService` for symbol discovery.

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Data;

namespace AICodingServices.SKPlugins.Plugins;

public sealed class SolutionIndexPlugin
{
    private readonly SolutionIndexQueryService _queryService;

    public SolutionIndexPlugin(SolutionIndexQueryService queryService)
    {
        _queryService = queryService;
    }

    [SKFunction("Query the watched solution index by scope")]
    [SKParameterName("scope")]
    [SKParameterName("value")]
    public SolutionIndexQueryResult Query(
        string scope = "solution",
        string? value = null,
        int maxFiles = 200,
        int maxSymbols = 500)
    {
        return _queryService.QueryIndex(scope, value, maxFiles, maxSymbols);
    }

    [SKFunction("Find indexed C# symbols by name")]
    [SKParameterName("text")]
    [SKParameterName("kind")]
    public IndexedSymbolSearchResult FindSymbols(
        string text,
        string? kind = null,
        string? namespaceName = null,
        string? containingType = null,
        int maxResults = 100)
    {
        return _queryService.FindSymbols(text, kind, namespaceName, containingType, maxResults);
    }

    [SKFunction("Get all references to an indexed symbol")]
    [SKParameterName("stableSymbolKey")]
    public IReadOnlyList<IndexedReferenceRow> FindReferences(
        string stableSymbolKey,
        int maxResults = 500)
    {
        return _queryService.ListReferences(stableSymbolKey).Take(maxResults).ToArray();
    }

    [SKFunction("Get callers of a method or constructor")]
    [SKParameterName("stableSymbolKey")]
    public IReadOnlyList<IndexedCallSiteRow> FindCallers(
        string stableSymbolKey,
        int maxResults = 500)
    {
        return _queryService.ListCallSites(stableKey: stableSymbolKey).Take(maxResults).ToArray();
    }

    [SKFunction("Get symbol relationships")]
    [SKParameterName("stableSymbolKey")]
    public IReadOnlyList<IndexedRelationshipRow> FindRelationships(
        string stableSymbolKey,
        string direction = "both",
        int maxResults = 500)
    {
        return _queryService.ListRelationships(stableSymbolKey, direction, null).Take(maxResults).ToArray();
    }
}
```

### 3. RoslynEditPlugin

Wraps `RoslynEditService` for typed C# edits.

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Workflow;

namespace AICodingServices.SKPlugins.Plugins;

public sealed class RoslynEditPlugin
{
    private readonly RoslynEditService _roslynService;

    public RoslynEditPlugin(MonitorSettings settings)
    {
        _roslynService = new RoslynEditService(settings);
    }

    [SKFunction("Get a source map for a C# file")]
    [SKParameterName("path")]
    [SKParameterName("mode")]
    public RoslynSourceMapResult GetSourceMap(
        string? path = null,
        string scope = "auto",
        string mode = "auto",
        string? namespaceName = null)
    {
        return _roslynService.GetSourceMap(path, scope, mode, namespaceName);
    }

    [SKFunction("Get a symbol body using a Roslyn selector")]
    [SKParameterName("path")]
    [SKParameterName("symbolSelectorJson")]
    public RoslynSymbolReadResult GetSymbol(
        string path,
        string? symbolName = null,
        string? symbolSelectorJson = null)
    {
        string selector = !string.IsNullOrWhiteSpace(symbolSelectorJson)
            ? symbolSelectorJson
            : JsonSerializer.Serialize(new RoslynSymbolSelector(Name: symbolName));
        return _roslynService.GetSymbol(path, selector);
    }

    [SKFunction("Submit a complete symbol replacement")]
    [SKParameterName("path")]
    [SKParameterName("symbolSelectorJson")]
    [SKParameterName("code")]
    public RoslynEditResult SubmitSymbol(
        string path,
        string symbolSelectorJson,
        string code,
        string? manifestJson = null)
    {
        return _roslynService.SubmitSymbol(path, symbolSelectorJson, code, manifestJson, validateSyntax: true);
    }

    [SKFunction("Add a using directive")]
    [SKParameterName("path")]
    [SKParameterName("namespace")]
    public RoslynEditResult AddUsing(string path, string @namespace)
    {
        return _roslynService.AddUsing(path, @namespace, null, validateSyntax: true);
    }

    [SKFunction("Remove a using directive")]
    [SKParameterName("path")]
    [SKParameterName("namespace")]
    public RoslynEditResult RemoveUsing(string path, string @namespace)
    {
        return _roslynService.RemoveUsing(path, @namespace, null, validateSyntax: true);
    }

    [SKFunction("Add a new member to a type")]
    [SKParameterName("path")]
    [SKParameterName("containingType")]
    [SKParameterName("symbolType")]
    [SKParameterName("code")]
    public RoslynEditResult AddSymbol(
        string path,
        string containingType,
        string symbolType,
        string code,
        string? afterSymbol = null)
    {
        return _roslynService.AddSymbol(path, containingType, symbolType, code, afterSymbol, null, validateSyntax: true);
    }

    [SKFunction("Remove a symbol using a Roslyn selector")]
    [SKParameterName("path")]
    [SKParameterName("symbolSelectorJson")]
    public RoslynEditResult RemoveSymbol(string path, string symbolSelectorJson)
    {
        return _roslynService.RemoveSymbol(path, symbolSelectorJson, null, validateSyntax: true);
    }
}
```

### 4. ValidationPlugin

Wraps pre-merge validation for SK planning.

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Workflow;

namespace AICodingServices.SKPlugins.Plugins;

public sealed class ValidationPlugin
{
    private readonly PreMergeValidationService _validationService;

    public ValidationPlugin()
    {
        _validationService = new PreMergeValidationService();
    }

    [SKFunction("Run pre-merge validation on a staged edit")]
    [SKParameterName("stagedRecordId")]
    public PreMergeValidationResult ValidateStaged(
        string stagedRecordId,
        IReadOnlyList<StagedEditRecord>? overlayRecords = null)
    {
        // This would need access to WorkflowEditService to fetch the record
        throw new NotImplementedException("Requires WorkflowEditService injection");
    }

    [SKFunction("Check if a Working file has syntax errors")]
    [SKParameterName("workingFilePath")]
    public EditValidationResult ValidateSyntax(string workingFilePath)
    {
        var validator = new CandidateEditValidator(new MonitorSettingsLoader().Load());
        return validator.ValidateSyntax(workingFilePath);
    }
}
```

### 5. SessionManagementPlugin

Manages edit sessions for SK workflows.

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Data;
using AICodingServices.Indexing;

namespace AICodingServices.SKPlugins.Plugins;

public sealed class SessionManagementPlugin
{
    private readonly SolutionIndexQueryService _queryService;

    public SessionManagementPlugin(SolutionIndexQueryService queryService)
    {
        _queryService = queryService;
    }

    [SKFunction("Create a new edit session with planned files")]
    [SKParameterName("purpose")]
    public AICodingServicesSessionState CreateSession(
        string purpose,
        IReadOnlyList<AICodingServicesSessionPlannedFileInput> filesPlanned)
    {
        string sessionId = $"session-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..48];
        // Implementation would write to session store
        throw new NotImplementedException("Requires session store integration");
    }

    [SKFunction("Record a decision on a staged edit")]
    [SKParameterName("stagedRecordId")]
    [SKParameterName("decision")]
    public ReviewDecisionWithIndexRefreshResult RecordDecision(
        string stagedRecordId,
        string decision,
        string? expectedStagedHash = null)
    {
        // Implementation via StagedDecisionWorkflow
        throw new NotImplementedException("Requires workflow integration");
    }

    [SKFunction("Launch staged diff for review")]
    [SKParameterName("stagedRecordId")]
    public StagedDiffLaunchWorkflowResult LaunchReview(
        string stagedRecordId,
        string? diffToolPath = null)
    {
        // Implementation via StagedDiffLaunchWorkflow
        throw new NotImplementedException("Requires workflow integration");
    }
}
```

---

## Planner Implementations

### Sequential Planner for Simple Edits

```csharp
using Microsoft.SemanticKernel.Planners.Sequential;
using Microsoft.SemanticKernel;

namespace AICodingServices.SKPlugins.Planners;

public sealed class EditWorkflowPlanner
{
    public static async Task<string> PlanSimpleEditAsync(
        Kernel kernel,
        string goal,
        CancellationToken cancellationToken = default)
    {
        var planner = new SequentialPlanner(kernel, new SequentialPlannerOptions
        {
            MaxTokens = 8000,
            RenamedFunctions = new Dictionary<string, string>
            {
                ["WorkflowEditPlugin-Refresh"] = "refresh_file",
                ["WorkflowEditPlugin-ReplaceText"] = "replace_text",
                ["WorkflowEditPlugin-Stage"] = "stage_candidate",
                ["SolutionIndexPlugin-Query"] = "query_index",
                ["SolutionIndexPlugin-FindSymbols"] = "find_symbols"
            }
        });

        var plan = await planner.CreatePlanAsync(goal, cancellationToken);
        return plan.ToString();
    }
}
```

### Stepwise Planner for Complex Refactoring

```csharp
using Microsoft.SemanticKernel.Planners.Stepwise;
using Microsoft.SemanticKernel;

namespace AICodingServices.SKPlugins.Planners;

public sealed class RefactorPlanner
{
    public static async Task<FunctionResult> PlanComplexRefactorAsync(
        Kernel kernel,
        string task,
        string? goal = null,
        CancellationToken cancellationToken = default)
    {
        var planner = new StepwisePlanner(kernel, new StepwisePlannerOptions
        {
            MaxTokens = 16000,
            MaxIterations = 15,
            BehaviorWhenExtendingPlan = PlanExtensions.BehaviorWhenExtendingPlan.ReplanOnFailure
        });

        return await planner.ExecuteAsync(task, goal, cancellationToken);
    }
}
```

---

## Memory Integration

### Session Memory Store

```csharp
using Microsoft.SemanticKernel.Memory;
using AICodingServices.Core;
using AICodingServices.Data;

namespace AICodingServices.SKPlugins.Memory;

public sealed class EditSessionMemoryStore : ISemanticTextMemory
{
    private readonly string _memoryRoot;
    private readonly Dictionary<string, SessionMemoryEntry> _sessions = new();

    public EditSessionMemoryStore(MonitorSettings settings)
    {
        _memoryRoot = Path.Combine(settings.RuntimeRoot, "sk-memory", "sessions");
        Directory.CreateDirectory(_memoryRoot);
    }

    public async Task<string> SaveSessionAsync(
        string sessionId,
        string purpose,
        IReadOnlyList<string> plannedFiles,
        CancellationToken cancellationToken = default)
    {
        var entry = new SessionMemoryEntry
        {
            SessionId = sessionId,
            Purpose = purpose,
            PlannedFiles = plannedFiles.ToList(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _sessions[sessionId] = entry;
        string path = GetSessionPath(sessionId);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entry), cancellationToken);
        return sessionId;
    }

    public async Task<SessionMemoryEntry?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var cached))
            return cached;

        string path = GetSessionPath(sessionId);
        if (!File.Exists(path))
            return null;

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        var entry = JsonSerializer.Deserialize<SessionMemoryEntry>(json);
        if (entry is not null)
            _sessions[sessionId] = entry;
        return entry;
    }

    private string GetSessionPath(string sessionId) =>
        Path.Combine(_memoryRoot, $"{Sanitize(sessionId)}.json");

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    // ISemanticTextMemory implementation stubs (delegate to actual memory store)
    public Task<string> SaveInformationAsync(string collection, string text, string id, string? description = null, CancellationToken cancellationToken = default) 
        => throw new NotImplementedException();
    
    public Task<string> SaveReferenceAsync(string collection, string text, string externalId, string externalSourceName, string? description = null, CancellationToken cancellationToken = default) 
        => throw new NotImplementedException();
    
    public Task<MemoryQueryResult?> GetAsync(string collection, string key, bool withEmbedding = false, CancellationToken cancellationToken = default) 
        => throw new NotImplementedException();
    
    public Task<MemoryQueryResultCollection> SearchAsync(string collection, string query, int limit = 1, double minRelevanceScore = 0.7, bool withEmbeddings = false, CancellationToken cancellationToken = default) 
        => throw new NotImplementedException();
    
    public Task RemoveAsync(string collection, string key, CancellationToken cancellationToken = default) 
        => throw new NotImplementedException();
    
    public IAsyncEnumerable<string> GetCollectionsAsync(CancellationToken cancellationToken = default) 
        => throw new NotImplementedException();
}

public sealed class SessionMemoryEntry
{
    public string SessionId { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public List<string> PlannedFiles { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<string> CompletedFiles { get; set; } = [];
    public List<string> RejectedFiles { get; set; } = [];
}
```

---

## Kernel Builder

```csharp
using Microsoft.SemanticKernel;
using AICodingServices.Core;
using AICodingServices.Data;

namespace AICodingServices.SKPlugins.Orchestration;

public sealed class AICodingServicesKernelBuilder
{
    public static Kernel CreateKernel(
        MonitorSettings settings,
        SolutionIndexQueryService queryService,
        string? openAiKey = null,
        string? azureOpenAiEndpoint = null)
    {
        var builder = Kernel.CreateBuilder();

        // Add AI service (prefer Azure OpenAI, fallback to OpenAI)
        if (!string.IsNullOrWhiteSpace(azureOpenAiEndpoint))
        {
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: "gpt-4",
                endpoint: azureOpenAiEndpoint,
                apiKey: openAiKey ?? throw new ArgumentNullException(nameof(openAiKey)));
        }
        else if (!string.IsNullOrWhiteSpace(openAiKey))
        {
            builder.AddOpenAIChatCompletion(
                modelId: "gpt-4",
                apiKey: openAiKey);
        }
        else
        {
            throw new InvalidOperationException("Either Azure OpenAI endpoint or OpenAI key is required.");
        }

        // Register services
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(queryService);

        // Add plugins
        builder.Plugins.AddFromType<WorkflowEditPlugin>(ServiceLifetime.Singleton);
        builder.Plugins.AddFromType<SolutionIndexPlugin>(ServiceLifetime.Singleton);
        builder.Plugins.AddFromType<RoslynEditPlugin>(ServiceLifetime.Singleton);
        builder.Plugins.AddFromType<ValidationPlugin>(ServiceLifetime.Singleton);
        builder.Plugins.AddFromType<SessionManagementPlugin>(ServiceLifetime.Singleton);

        // Add memory
        builder.Services.AddSingleton<ISemanticTextMemory>(sp =>
            new EditSessionMemoryStore(settings));

        return builder.Build();
    }
}
```

---

## Usage Examples

### Simple Text Edit

```csharp
// Initialize kernel
Kernel kernel = AICodingServicesKernelBuilder.CreateKernel(settings, queryService, openAiKey);

// Define the task
string prompt = """
    Replace the ILogger field declaration in Program.cs with IMonitorLogger.
    1. First refresh the file
    2. Replace 'private readonly ILogger' with 'private readonly IMonitorLogger'
    3. Update the constructor parameter from ILogger to IMonitorLogger
    4. Stage the candidate
    """;

// Execute via planner
var result = await kernel.InvokeAsync("WorkflowEditPlugin-ReplaceText", new KernelArguments
{
    ["path"] = "Program.cs",
    ["oldText"] = "private readonly ILogger",
    ["newText"] = "private readonly IMonitorLogger"
});
```

### Complex Multi-File Refactoring

```csharp
// Use stepwise planner for complex tasks
string task = """
    Refactor all usages of the deprecated ILoggingService to use the new IMonitorLogger.
    This affects:
    - Provider.cs (the interface definition)
    - Consumer.cs (uses ILoggingService)
    - Presenter.cs (uses ILoggingService)
    
    For each file:
    1. Refresh from watched source
    2. Find all references to ILoggingService
    3. Replace with IMonitorLogger
    4. Update any using directives
    5. Stage for review
    """;

var planner = new StepwisePlanner(kernel, new StepwisePlannerOptions
{
    MaxIterations = 20
});

var result = await planner.ExecuteAsync(task);
Console.WriteLine(result);
```

### Symbol-Based Edit

```csharp
// Find and edit a specific symbol
var symbols = await kernel.InvokeAsync("SolutionIndexPlugin-FindSymbols", new KernelArguments
{
    ["text"] = "ProcessPayment",
    ["kind"] = "method"
});

var symbolKey = symbols.Get<List<object>>()[0].StableKey;

var sourceMap = await kernel.InvokeAsync("RoslynEditPlugin-GetSourceMap", new KernelArguments
{
    ["path"] = "PaymentService.cs",
    ["mode"] = "selector"
});

var symbolBody = await kernel.InvokeAsync("RoslynEditPlugin-GetSymbol", new KernelArguments
{
    ["path"] = "PaymentService.cs",
    ["symbolSelectorJson"] = symbolKey
});

// Submit modified symbol
var editResult = await kernel.InvokeAsync("RoslynEditPlugin-SubmitSymbol", new KernelArguments
{
    ["path"] = "PaymentService.cs",
    ["symbolSelectorJson"] = symbolKey,
    ["code"] = modifiedCode
});
```

---

## Integration with Existing MCP Server

The SK plugin layer can coexist with the existing MCP server:

```csharp
// Program.cs extension
public static async Task Main(string[] args)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    
    // ... existing MCP setup ...
    
    // Add SK kernel
    if (builder.Services.GetService<MonitorSettings>() is { } settings)
    {
        var queryService = new SolutionIndexQueryService(settings);
        var kernel = AICodingServicesKernelBuilder.CreateKernel(
            settings, 
            queryService,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        
        builder.Services.AddSingleton(kernel);
    }
    
    await builder.Build().RunAsync();
}
```

---

## Testing Strategy

### Unit Tests

```csharp
public sealed class WorkflowEditPluginTests
{
    [Fact]
    public async Task Refresh_creates_Working_candidate()
    {
        // Arrange
        var settings = CreateTestSettings();
        var plugin = new WorkflowEditPlugin(settings);
        
        // Act
        var result = plugin.Refresh("Program.cs");
        
        // Assert
        Assert.True(result.WorkingFileExists);
        Assert.False(result.RequiresRefresh);
    }
    
    [Fact]
    public async Task ReplaceText_with_invalid_oldText_throws()
    {
        // Arrange
        var settings = CreateTestSettings();
        var plugin = new WorkflowEditPlugin(settings);
        plugin.Refresh("Program.cs");
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            plugin.ReplaceText("Program.cs", "nonexistent", "replacement"));
    }
}
```

### Integration Tests

```csharp
public sealed class SKPlanningIntegrationTests
{
    [Fact]
    public async Task SequentialPlanner_can_plan_simple_edit()
    {
        // Arrange
        var kernel = AICodingServicesKernelBuilder.CreateKernel(testSettings, queryService, apiKey);
        
        // Act
        var plan = await EditWorkflowPlanner.PlanSimpleEditAsync(
            kernel,
            "Refresh Program.cs and replace 'Hello' with 'World'");
        
        // Assert
        Assert.Contains("refresh_file", plan);
        Assert.Contains("replace_text", plan);
    }
}
```

---

## Migration Checklist

- [ ] Add `Microsoft.SemanticKernel` NuGet package to new project
- [ ] Create `AICodingServices.SKPlugins.csproj`
- [ ] Implement `WorkflowEditPlugin`
- [ ] Implement `SolutionIndexPlugin`
- [ ] Implement `RoslynEditPlugin`
- [ ] Implement `ValidationPlugin`
- [ ] Implement `SessionManagementPlugin`
- [ ] Create `AICodingServicesKernelBuilder`
- [ ] Add unit tests for all plugins
- [ ] Add integration tests for planners
- [ ] Update `AICodingServices.slnx` to include new project
- [ ] Update documentation

---

## Open Questions

1. **Multi-model routing**: Should SK route to different models based on task complexity?
2. **Planner persistence**: Should plan state be persisted across sessions?
3. **MCP vs SK**: Should existing MCP tools be deprecated in favor of SK plugins?
4. **Error recovery**: How should SK handle planning failures mid-execution?

---

## References

- [Semantic Kernel Documentation](https://docs.microsoft.com/semantic-kernel/)
- [Semantic Kernel GitHub](https://github.com/microsoft/semantic-kernel)
- [AICodingServices MCP Server](../AICodingServices.McpServer/Program.cs)
- [WorkflowEditService](../AICodingServices.Workflow/WorkflowEditService.cs)
