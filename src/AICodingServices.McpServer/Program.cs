using AICodingServices.Core;
using AICodingServices.Data;
using AICodingServices.Indexing;
using AICodingServices.Logging;
using AICodingServices.Runtime;
using AICodingServices.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICodingServices.McpServer;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        MonitorSettings settings = LoadSettings(args);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<IMonitorLogger>(_ => new MonitorLogPipeClientLogger(
            MonitorLogPipeNames.GetDefaultPipeName(settings),
            new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings))));
        builder.Services.AddSingleton(SolutionIndexQueryService.Create(settings));
        builder.Services.AddSingleton(new WorkflowEditService(settings));
        builder.Services.AddSingleton(new RoslynEditService(settings));
        builder.Services.AddSingleton(new DemoWorkspaceService(settings));
        builder.Services.AddSingleton(new WorkflowEditPaths(settings));
        builder.Services.AddSingleton<AICodingServicesMcpRuntimeState>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<AICodingServicesTools>();

        await builder.Build().RunAsync();
    }

    private static MonitorSettings LoadSettings(string[] args)
    {
        string repositoryRoot = GetOption(args, "--repo-root") ?? Directory.GetCurrentDirectory();
        string? settingsPath = GetOption(args, "--config");
        return MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

[McpServerToolType]
public sealed partial class AICodingServicesTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MonitorSettings settings;
    private readonly SolutionIndexQueryService queryService;
    private readonly WorkflowEditService workflowService;
    private readonly RoslynEditService roslynEditService;

    private readonly SessionIntentPolicyService sessionIntentPolicyService = new();

    private readonly DemoWorkspaceService demoWorkspaceService;
    private readonly WorkflowEditPaths workflowPaths;
    private readonly AICodingServicesMcpRuntimeState runtimeState;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly IMonitorLogger logger;

    public AICodingServicesTools(
        MonitorSettings settings,
        SolutionIndexQueryService queryService,
        WorkflowEditService workflowService,
        RoslynEditService roslynEditService,
        DemoWorkspaceService demoWorkspaceService,
        WorkflowEditPaths workflowPaths,
        AICodingServicesMcpRuntimeState runtimeState,
        IHostApplicationLifetime applicationLifetime,
        IMonitorLogger logger)
    {
        this.settings = settings;
        this.queryService = queryService;
        this.workflowService = workflowService;
        this.roslynEditService = roslynEditService;
        this.demoWorkspaceService = demoWorkspaceService;
        this.workflowPaths = workflowPaths;
        this.runtimeState = runtimeState;
        this.applicationLifetime = applicationLifetime;
        this.logger = logger;
    }

    [McpServerTool]
    [Description("Initialize Coding Services through Semantic Kernel. This keeps a healthy CodexUI alive, only restarts it when the site probe fails, and logs each startup step while the workflow runs.")]
    public async Task<string> InitializeCodingServices(
        [Description("Optional repository root override. Defaults to the current monitor repository root.")] string? repositoryRoot = null,
        [Description("Optional explicit appsettings.json path. Defaults to the active monitor config.")] string? settingsPath = null,
        [Description("Optional explicit site URL. Defaults to http://localhost:5000/.")] string siteUrl = "http://localhost:5000/",
        [Description("When the site probe fails, stop discovered CodexUI processes before restart.")] bool stopExistingProcesses = true,
        [Description("When the site probe fails, attempt to start CodexUI again.")] bool startCodexUi = true,
        [Description("Whether to ask the host for a thread remount before direct bridge fallback. In-process MCP calls cannot reset the host thread, so this is informational here.")] bool attemptThreadReset = true)
    {
        runtimeState.Touch();

        string resolvedRepositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot)
            ? settings.RepositoryRoot
            : Path.GetFullPath(repositoryRoot);
        string resolvedSettingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(resolvedRepositoryRoot, "config", "appsettings.json")
            : Path.GetFullPath(settingsPath);

        CodingServicesSessionStartupWorkflow workflow = new(
            new LocalCodingServicesSessionStartupRuntime(),
            new AttachedMcpSessionHostController());
        CodingServicesSessionStartupPlugin plugin = new(
            workflow,
            (step, cancellationToken) =>
            {
                LogStartupStep(step, resolvedRepositoryRoot, resolvedSettingsPath);
                return Task.CompletedTask;
            });

        Kernel kernel = Kernel.CreateBuilder().Build();
        kernel.Plugins.AddFromObject(plugin, "CodingServicesSessionStartup");

        KernelArguments arguments = new()
        {
            ["repositoryRoot"] = resolvedRepositoryRoot,
            ["settingsPath"] = resolvedSettingsPath,
            ["siteUrl"] = siteUrl,
            ["stopExistingProcesses"] = stopExistingProcesses,
            ["startCodexUi"] = startCodexUi,
            ["attemptThreadReset"] = attemptThreadReset
        };

        FunctionResult result = await kernel.InvokeAsync(
            "CodingServicesSessionStartup",
            "initialize_coding_services",
            arguments);
        string? message = result.GetValue<string>();
        return string.IsNullOrWhiteSpace(message)
            ? "Initialize Coding Services completed without a message."
            : message;
    }

    [McpServerTool]
    [Description("Rebuild the monitor-owned SQLite index for the watched solution.")]
    public async Task<object> RefreshSolutionIndex()
    {
        runtimeState.Touch();
        try
        {
            return await RefreshSolutionIndexCore();
        }
        catch (Exception ex) when (IsExpectedToolFailure(ex))
        {
            return CreateToolError(ex, "The solution index rebuild should complete or return a structured failure with the exception message.");
        }
    }

    private async Task<AICodingServicesRefreshIndexResult> RefreshSolutionIndexCore()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        SolutionIndexSummary summary = await new SolutionIndexRebuildService().RebuildAsync(settings);
        stopwatch.Stop();
        return new AICodingServicesRefreshIndexResult(summary, queryService.GetMonitorStatus(), stopwatch.ElapsedMilliseconds);
    }

    [McpServerTool]
    [Description("Refresh one watched C# file in the monitor-owned SQLite solution index. AICodingServices currently rebuilds the semantic index and returns the requested file slice.")]
    public async Task<object> RefreshSolutionIndexFile(
        [Description("Watched C# file path, absolute or relative to the watched solution folder.")] string path)
    {
        runtimeState.Touch();
        try
        {
            return await RefreshSolutionIndexFileCore(path);
        }
        catch (Exception ex) when (IsExpectedToolFailure(ex))
        {
            return CreateToolError(ex, "The file index refresh should complete or return a structured failure with the exception message.", path);
        }
    }

    private async Task<AICodingServicesRefreshIndexFileResult> RefreshSolutionIndexFileCore(string path)
    {
        AICodingServicesRefreshIndexResult refresh = await RefreshSolutionIndexCore();
        IndexedFileDetailResult detail = queryService.GetFileDetail(path);
        return new AICodingServicesRefreshIndexFileResult(
            refresh.Summary,
            refresh.Status,
            refresh.ElapsedMilliseconds,
            detail,
            detail.Files,
            detail.Symbols);
    }

    [McpServerTool]
    [Description("Refresh a watched source file into the monitor-owned Working folder, then refresh the same file in the monitor-owned SQLite solution index.")]
    public async Task<AICodingServicesRefreshFileAndIndexResult> RefreshFileAndIndex(
        [Description("Watched source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        runtimeState.Touch();
        EditSessionStatus refresh = workflowService.Refresh(ResolveWatchedPath(sourceFilePath));
        AICodingServicesRefreshIndexFileResult index = await RefreshSolutionIndexFileCore(sourceFilePath);
        return new AICodingServicesRefreshFileAndIndexResult(refresh, index);
    }

    [McpServerTool]
    [Description("Return status for the monitor-owned watched solution index, including database path and indexed counts.")]
    public MonitorStatusResult GetSolutionIndexStatus()
    {
        runtimeState.Touch();
        return queryService.GetMonitorStatus();
    }

    [McpServerTool]
    [Description("Return the monitor-owned watched solution index as compact JSON with indexed files and symbols. Use maxFiles/maxSymbols to budget the payload.")]
    public SolutionIndexQueryResult GetSolutionIndex(
        [Description("Maximum files to return.")] int maxFiles = 5000,
        [Description("Maximum symbols to return.")] int maxSymbols = 50000)
    {
        runtimeState.Touch();
        return queryService.QueryIndex(maxFiles: maxFiles, maxSymbols: maxSymbols);
    }

    [McpServerTool]
    [Description("Return the monitor-owned watched solution index tree as compact JSON: projects, namespaces, and files.")]
    public AICodingServicesSolutionIndexTree GetSolutionIndexTree()
    {
        runtimeState.Touch();
        IReadOnlyList<IndexedProjectRow> projects = queryService.ListProjects();
        IReadOnlyList<IndexedDocumentRow> documents = queryService.ListDocuments();
        IReadOnlyList<AICodingServicesNamespaceTree> namespaces = queryService.ListSymbols()
            .GroupBy(symbol => symbol.Namespace)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AICodingServicesNamespaceTree(
                group.Key,
                group.Select(symbol => symbol.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                group.Count()))
            .ToArray();

        return new AICodingServicesSolutionIndexTree(projects, documents, namespaces);
    }

    [McpServerTool]
    [Description("Query the monitor-owned watched solution index by scope. Scopes: solution, namespace, folder, file.")]
    public SolutionIndexQueryResult QuerySolutionIndex(
        [Description("Index scope: solution, namespace, folder, or file.")] string scope = "solution",
        [Description("Namespace text, folder path, or file path for scoped queries. Omit for solution scope.")] string? value = null,
        [Description("Maximum files to return.")] int maxFiles = 200,
        [Description("Maximum symbols to return.")] int maxSymbols = 500)
    {
        runtimeState.Touch();
        return queryService.QueryIndex(scope, value, maxFiles, maxSymbols);
    }

    [McpServerTool]
    [Description("Find indexed C# symbols by name text, optional kind, optional exact namespace, and optional containing type using the monitor-owned watched solution index. Qualified Type.Member text is treated as a containing-type member lookup.")]
    public IndexedSymbolSearchResult FindIndexedSymbols(
        [Description("Symbol name text to search for. Use Type.Member to avoid homonym fanout for members.")] string text,
        [Description("Optional exact symbol kind, such as class, method, property, field, constructor, enum, delegate, interface, struct, or record.")] string? kind = null,
        [Description("Optional exact namespace filter.")] string? namespaceName = null,
        [Description("Optional containing type filter, such as OrderRepository or My.Namespace.OrderRepository.")] string? containingType = null,
        [Description("Maximum symbols to return.")] int maxResults = 100)
    {
        runtimeState.Touch();
        return queryService.FindSymbols(text, kind, namespaceName, containingType, maxResults);
    }

    [McpServerTool]
    [Description("Return one indexed C# symbol by stable symbol key from the monitor-owned watched solution index.")]
    public object? GetIndexedSymbol(
        [Description("Stable symbol key returned by query_solution_index or find_indexed_symbols.")] string stableSymbolKey)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.FindSymbols(string.Empty, maxResults: 50000)
            .Symbols
            .FirstOrDefault(symbol => symbol.Symbol.StableKey.Equals(stableSymbolKey, StringComparison.Ordinal));
    }

    [McpServerTool]
    [Description("Return persisted indexed reference sites for one stable C# symbol key. Lean shape omits repeated project path and file hash fields; rich shape returns complete stored rows.")]
    public object FindIndexedReferences(
        [Description("Stable symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Maximum reference rows to return.")] int maxResults = 500,
        [Description("Response shape: lean or rich. Lean is optimized for MCP token cost; rich preserves every persisted reference row field.")] string responseShape = "lean")
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        IndexedReferenceRow[] references = queryService.ListReferences(stableSymbolKey).Take(maxResults).ToArray();
        if (responseShape.Equals("rich", StringComparison.OrdinalIgnoreCase))
        {
            return references;
        }

        return references.Select(ToMcpReferenceRow).ToArray();
    }

    [McpServerTool]
    [Description("Return persisted indexed invocation/object-creation call sites for one stable C# method or constructor symbol key, including caller identity.")]
    public object FindIndexedCallers(
        [Description("Stable method or constructor symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Maximum caller rows to return.")] int maxResults = 500)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.ListCallSites(stableKey: stableSymbolKey)
            .Take(maxResults)
            .ToArray();
    }

    [McpServerTool]
    [Description("Return persisted indexed symbol relationship rows for one stable symbol key, including incoming and outgoing relationship direction.")]
    public object FindIndexedRelationships(
        [Description("Stable symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Optional exact relationship kind filter.")] string? relationshipKind = null,
        [Description("Relationship direction: outgoing, incoming, or both.")] string direction = "both",
        [Description("Maximum relationship rows to return.")] int maxResults = 500)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.ListRelationships(stableSymbolKey, direction, relationshipKind)
            .Take(maxResults)
            .ToArray();
    }

    [McpServerTool]
    [Description("List staged edit records owned by a durable monitor session.")]
    public IReadOnlyList<StagedEditRecord> ListSessionStagedRecords(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        _ = GetMonitorSession(sessionId);
        return workflowService.ListStagedRecords(sessionId);
    }

    [McpServerTool]
    [Description("Refresh a watched source file into the monitor-owned Working folder and clear candidate state for that file.")]
    public EditSessionStatus RefreshFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(sourceFilePath);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.Refresh);
        EditSessionStatus status = workflowService.Refresh(fullPath);
        RecordMonitorSessionEvent(sessionId, "refresh-file", status.WatchedFilePath, JsonSerializer.Serialize(status, JsonOptions));
        return status;
    }

    [McpServerTool]
    [Description("Create a new-file edit session with an empty monitor-owned Working candidate. Watched source is not created.")]
    public EditSessionStatus NewFile(
        [Description("Future watched source path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(sourceFilePath);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.Refresh);
        EditSessionStatus status = workflowService.NewFile(fullPath);
        RecordMonitorSessionEvent(sessionId, "new-file", status.WatchedFilePath, JsonSerializer.Serialize(status, JsonOptions));
        return status;
    }

    [McpServerTool]
    [Description("Read a watched source file through the Monitor MCP server and record it against a monitor session.")]
    public AICodingServicesFileReadResult GetFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        string path = ResolveWatchedPath(sourceFilePath);
        EnsurePlannedMutationAllowed(sessionId, path, SessionEditOperationFamily.Read);
        string text = File.ReadAllText(path);
        AICodingServicesFileHashInfo hashInfo = GetFileHashInfo(path);
        AICodingServicesSessionFileAccess access = RecordSessionFileAccess(sessionId, path, "read", hashInfo);
        RecordMonitorSessionEvent(sessionId, "file-fetch", path, JsonSerializer.Serialize(hashInfo, JsonOptions));
        return new AICodingServicesFileReadResult(path, workflowPaths.GetRelativeWatchedPath(path), hashInfo, access, text);
    }
    [McpServerTool]
    [Description("Create or update a local demo/proposal file under runtime/demos. Demo files are MCP-owned communication artifacts and are not watched source or staged review candidates.")]
    public DemoWorkspaceWriteResult WriteDemoFile(
    [Description("Demo file path relative to runtime/demos, such as proposals/example.md.")] string relativePath,
    [Description("Complete demo/proposal file content.")] string content,
    [Description("Short purpose shown in the Source page and demo metadata.")] string? purpose = null,
    [Description("Optional author label. Defaults to agent.")] string? author = null,
    [Description("Optional watched source paths this demo relates to.")] IReadOnlyList<string>? relatedSourcePaths = null)
    {
        runtimeState.Touch();
        return demoWorkspaceService.WriteDemoFile(relativePath, content, purpose, author, relatedSourcePaths);
    }

    [McpServerTool]
    [Description("Delete a local demo/proposal file under runtime/demos, including its metadata sidecar when present.")]
    public DemoWorkspaceDeleteResult DeleteDemoFile(
    [Description("Demo file path relative to runtime/demos.")] string relativePath)
    {
        runtimeState.Touch();
        return demoWorkspaceService.DeleteDemoFile(relativePath);
    }

    [McpServerTool]
    [Description("Read a local demo/proposal file from runtime/demos by relative path.")]
    public DemoWorkspaceFile ReadDemoFile(
    [Description("Demo file path relative to runtime/demos.")] string relativePath)
    {
        runtimeState.Touch();
        return demoWorkspaceService.ReadDemoFile(relativePath);
    }

    [McpServerTool]
    [Description("List local demo/proposal files under runtime/demos without returning file content.")]
    public IReadOnlyList<DemoWorkspaceFileSummary> ListDemoFiles(
    [Description("Maximum demo files to return.")] int maxResults = 200)
    {
        runtimeState.Touch();
        return demoWorkspaceService.ListDemoFiles(maxResults);
    }

    [McpServerTool]
    [Description("Check whether a watched source file has changed since it was last fetched in a durable monitor session.")]
    public AICodingServicesFileHashCheckResult CheckFileHash(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        runtimeState.Touch();
        AICodingServicesSessionState session = GetMonitorSession(sessionId);
        string path = ResolveWatchedPath(sourceFilePath);
        AICodingServicesFileHashInfo current = GetFileHashInfo(path);
        AICodingServicesSessionFileAccess? access = session.Files
            .Where(item => item.SourceFilePath.Equals(path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.LastAccessedAtUtc)
            .FirstOrDefault();
        AICodingServicesFileHashInfo? previous = access?.Hash;

        return new AICodingServicesFileHashCheckResult(
            path,
            previous is not null,
            previous?.Sha256.Equals(current.Sha256, StringComparison.OrdinalIgnoreCase) == false,
            current,
            previous,
            access);
    }

    [McpServerTool]
    [Description("Find source or related files under the watched project folder by filename or wildcard pattern.")]
    public IReadOnlyList<AICodingServicesFileMatch> FindFile(
        [Description("Filename or wildcard pattern, such as Program.cs or *.razor.")] string fileNameOrPattern,
        [Description("Maximum number of matches to return.")] int maxResults = 25)
    {
        runtimeState.Touch();
        string pattern = string.IsNullOrWhiteSpace(fileNameOrPattern) ? "*" : fileNameOrPattern;
        return Directory.Exists(settings.WatchedProjectFolder)
            ? Directory.EnumerateFiles(settings.WatchedProjectFolder, pattern, SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOrHiddenDirectory(path))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(path => new AICodingServicesFileMatch(Path.GetFileName(path), path, workflowPaths.GetRelativeWatchedPath(path)))
                .ToArray()
            : [];
    }

    [McpServerTool]
    [Description("Return a Roslyn-derived outline for a watched C# source file, including kind, name, span, signature, namespace, and containing type.")]
    public RoslynFileOutlineResult GetFileOutline(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        return roslynEditService.GetFileOutline(fullPath);
    }

    [McpServerTool]
    [Description("Return a Roslyn-derived source map for a C# file, folder, namespace, or watched project. Use selector mode before C# symbol edits.")]
    public object GetSourceMap(
        [Description("Optional source file/folder path, or namespace text when scope is namespace.")] string? path = null,
        [Description("Source map scope: auto, file, folder, namespace, or project.")] string scope = "auto",
        [Description("Source map density: auto, navigation, selector, detail, or full.")] string mode = "auto",
        [Description("Optional namespace text when scope is namespace.")] string? namespaceName = null,
        [Description("Symbol ordering for chunked results: declaration, name, type-then-member, edit-priority, or diagnostics.")] string orderBy = "declaration",
        [Description("Number of ordered symbols to skip before returning this source-map page.")] int skipSymbols = 0,
        [Description("Maximum symbols to return in this source-map page. Use 0 for automatic budgeted behavior.")] int maxSymbols = 0,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        RoslynSourceMapResult result;
        try
        {
            result = roslynEditService.GetSourceMap(path, scope, mode, namespaceName, orderBy, skipSymbols, maxSymbols);
        }
        catch (InvalidOperationException ex) when (IsRecoverableRoslynGuidanceError(ex))
        {
            return new AICodingServicesToolErrorResult(true, ex.Message, "Use .cs/.razor.cs for Roslyn symbol tools or text/file workflow tools for markup.", path);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "get-source-map", path ?? settings.WatchedProjectFolder, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("Read one C# symbol body from the monitor-owned Working candidate using a Roslyn selector.")]
    public object GetSymbol(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Compatibility shortcut symbol name.")] string? symbolName = null,
        [Description("Structured selector JSON from get_source_map when available.")] string? symbolSelectorJson = null,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        string selector = !string.IsNullOrWhiteSpace(symbolSelectorJson)
            ? symbolSelectorJson
            : JsonSerializer.Serialize(new RoslynSymbolSelector(Name: symbolName), JsonOptions);
        RoslynSymbolReadResult result;
        try
        {
            result = roslynEditService.GetSymbol(ResolveWatchedPath(path), selector);
        }
        catch (InvalidOperationException ex) when (IsRecoverableRoslynGuidanceError(ex))
        {
            return new AICodingServicesToolErrorResult(true, ex.Message, "Use .cs/.razor.cs for Roslyn symbol tools or text/file workflow tools for markup.", path);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "get-symbol", result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("Return edit workflow status for one watched source file.")]
    public EditSessionStatus GetEditStatus(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        EditSessionStatus status = workflowService.GetStatus(ResolveWatchedPath(sourceFilePath));
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "get-edit-status", status.WatchedFilePath, JsonSerializer.Serialize(status, JsonOptions));
        }

        return status;
    }

    [McpServerTool]
    [Description("Write a full-file candidate into Working. Policy: use for new files, generated files, or explicit whole-file fallbacks; existing C# source normally blocks this edit family.")]
    public EditSessionStatus SubmitFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Complete replacement file content.")] string content,
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.WholeFile, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        EditSessionStatus status = workflowService.SubmitFile(fullPath, content, manifestJson, !deferOverlayValidation);
        RecordMonitorSessionEvent(sessionId, "submit-file", fullPath, manifestJson);
        return status;
    }

    [McpServerTool]
    [Description("Replace exact oldText in Working. Policy: normal for Markdown/Razor/markup/config; blocked for existing C# source unless the session policy explicitly allows text fallback.")]
    public ReplaceTextResult ReplaceTextInFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Exact old text to replace using ordinal matching.")] string oldText,
        [Description("Replacement text.")] string newText,
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Required number of matches in the current edit base. Leave -1 for unique replacement when occurrenceIndex is unset, or no total-match assertion when occurrenceIndex is set.")] int expectedMatches = -1,
        [Description("Optional 0-based occurrence index. Leave -1 for unique/global replacement; set 0 or greater to replace one occurrence without requiring unique oldText.")] int occurrenceIndex = -1,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional SHA-256 hash of oldText.")] string? expectedOldTextHash = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        if (!string.IsNullOrWhiteSpace(expectedOldTextHash)
            && !ComputeHash(oldText).Equals(expectedOldTextHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("oldText hash did not match expectedOldTextHash.");
        }

        int? expectedMatchCount = expectedMatches >= 0
            ? expectedMatches
            : occurrenceIndex >= 0 ? null : 1;

        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.TextReplace, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        ReplaceTextResult result = workflowService.ReplaceText(
            fullPath,
            oldText,
            newText,
            expectedMatchCount,
            expectedFileHash,
            occurrenceIndex >= 0 ? occurrenceIndex : null,
            manifestJson,
            !deferOverlayValidation);
        RecordMonitorSessionEvent(sessionId, "replace-text-in-file", result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    [McpServerTool]
    [Description("Find exact text in the current Working candidate and return 1-based line/column bounds.")]
    public TextSpanResult FindTextSpan(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Exact text to find using ordinal matching.")] string findText,
        [Description("0-based occurrence index when text appears multiple times.")] int occurrenceIndex = 0,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional durable session handle.")] string? sessionId = null)
    {
        runtimeState.Touch();
        _ = sessionId;
        string fullPath = ResolveWatchedPath(path);
        EnsureSession(fullPath);
        return workflowService.FindTextSpan(fullPath, findText, occurrenceIndex, expectedFileHash);
    }

    [McpServerTool]
    [Description("Replace an exact 1-based line/column span in Working. Policy: bounded fallback for C# when Roslyn selectors are unsuitable; normal bounded edit for text/config/markup.")]
    public EditSessionStatus ReplaceSpanInFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("1-based start line.")] int startLine,
        [Description("1-based start column.")] int startColumn,
        [Description("1-based exclusive end line.")] int endLine,
        [Description("1-based exclusive end column.")] int endColumn,
        [Description("Replacement text.")] string newText,
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional SHA-256 hash of the extracted old span text.")] string? expectedOldTextHash = null,
        [Description("Optional exact old span text.")] string? expectedOldText = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.Span, manifestJson);
        EnsureSession(fullPath);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        EditSessionStatus status = workflowService.ReplaceSpan(
            fullPath,
            startLine,
            startColumn,
            endLine,
            endColumn,
            newText,
            expectedFileHash,
            expectedOldTextHash,
            expectedOldText,
            manifestJson,
            !deferOverlayValidation);
        RecordMonitorSessionEvent(sessionId, "replace-span-in-file", status.WatchedFilePath, null);
        return status;
    }

    [McpServerTool]
    [Description("Stage the current Working mirror candidate for review. This creates one immutable staged record from the completed candidate.")]
    public AICodingServicesStageCandidateResult StageCandidateForReview(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Optional compact ledger summary.")] string? ledgerSummary = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.Stage, manifestJson);
        StagedEditRecord record = workflowService.Stage(fullPath, ledgerSummary, sessionId);
        RecordMonitorSessionEvent(sessionId, "stage-candidate-for-review", record.StagedRecordId, JsonSerializer.Serialize(record, JsonOptions));

        StagedEditSummary summary = workflowService.CreateSummary(record);
        return new AICodingServicesStageCandidateResult(
            summary.StagedRecordId,
            summary.StagedHash,
            summary.Status,
            summary.Classification,
            summary.RecordPath,
            summary,
            verbose ? record : null,
            "Candidate staged. Use get_staged_record for full details or launch_staged_diff for review.");
    }

    [McpServerTool]
    [Description("Replace one C# symbol in Working using a Roslyn selector. Preferred for C# method replacement, method-body, signature, and symbol edits.")]
    public RoslynEditResult SubmitSymbol(string path, string symbolSelectorJson, string code, string sessionId, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.SubmitSymbol(fullPath, symbolSelectorJson, code, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "submit-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a using directive to Working through the Roslyn edit family. Preferred for C# source changes.")]
    public RoslynEditResult AddUsing(string path, string @namespace, string sessionId, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.AddUsing(fullPath, @namespace, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "add-using", result);
        return result;
    }

    [McpServerTool]
    [Description("Remove a using directive from Working through the Roslyn edit family. Preferred for C# source changes.")]
    public RoslynEditResult RemoveUsing(string path, string @namespace, string sessionId, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.RemoveUsing(fullPath, @namespace, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "remove-using", result);
        return result;
    }

    [McpServerTool]
    [Description("Add or remove the partial modifier on a C# type in Working through the Roslyn edit family.")]
    public RoslynEditResult SetTypePartial(string path, string containingType, bool isPartial, string sessionId, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.SetTypePartial(fullPath, containingType, isPartial, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "set-type-partial", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# member or nested type to a containing type in Working. Preferred over text/span edits for C# symbol additions.")]
    public RoslynEditResult AddSymbol(string path, string containingType, string symbolType, string code, string sessionId, string? afterSymbol = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.AddSymbol(fullPath, containingType, symbolType, code, afterSymbol, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "add-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# field to a containing type in Working through the Roslyn edit family.")]
    public RoslynEditResult AddField(string path, string containingType, string declaration, string sessionId, string? afterSymbol = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.AddField(fullPath, containingType, declaration, afterSymbol, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "add-field", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# property to a containing type in Working through the Roslyn edit family.")]
    public RoslynEditResult AddProperty(string path, string containingType, string declaration, string sessionId, string? afterSymbol = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.AddProperty(fullPath, containingType, declaration, afterSymbol, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "add-property", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# method to a containing type in Working through the Roslyn edit family; preferred for C# method additions.")]
    public RoslynEditResult AddMethod(string path, string containingType, string declaration, string sessionId, string? afterSymbol = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.AddMethod(fullPath, containingType, declaration, afterSymbol, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "add-method", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# constructor to a containing type in Working through the Roslyn edit family.")]
    public RoslynEditResult AddConstructor(string path, string containingType, string declaration, string sessionId, string? afterSymbol = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.AddConstructor(fullPath, containingType, declaration, afterSymbol, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "add-constructor", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# nested type to a containing type in Working through the Roslyn edit family.")]
    public RoslynEditResult AddNestedType(string path, string containingType, string declaration, string sessionId, string? afterSymbol = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.AddNestedType(fullPath, containingType, declaration, afterSymbol, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "add-nested-type", result);
        return result;
    }

    [McpServerTool]
    [Description("Remove one C# symbol from Working using a Roslyn selector. Preferred over text/span edits for C# symbol removals.")]
    public RoslynEditResult RemoveSymbol(string path, string symbolSelectorJson, string sessionId, string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        RoslynEditResult result = roslynEditService.RemoveSymbol(fullPath, symbolSelectorJson, manifestJson, !deferOverlayValidation);
        RecordRoslynSessionEvent(sessionId, "remove-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Classify a completed staged review for a staged edit. Accepted decisions require the expected staged hash.")]
    public ReviewDecisionWithIndexRefreshResult RecordDiffDecision(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("Operator-reported outcome: accepted or rejected.")] string decision,
        [Description("Expected staged hash for accepted decisions.")] string? expectedStagedHash = null,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        PlannedSessionDecisionOptions decisionOptions = BuildPlannedSessionDecisionOptions(stagedRecordId, decision);
        return new StagedDecisionWorkflow().Record(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            decision,
            expectedStagedHash,
            "AICodingServices.McpServer",
            decisionOptions.DeferIndexRefresh,
            decisionOptions.RefreshPlan,
            verbose,
            decisionOptions.TerminalValidationRecords);
    }

    [McpServerTool]
    [Description("Run pre-merge validation, then launch the configured review surface for a staged edit record. Browser review is the default; WinMerge remains available as an explicit fallback.")]
    public AICodingServicesStagedDiffLaunchResult LaunchStagedDiff(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("Explicit diff tool executable path.")] string? diffToolPath = null,
        [Description("Force launch after an explicit human validation override.")] bool forceValidation = false,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        StagedEditRecord stagedRecord = workflowService.GetStagedRecord(stagedRecordId);
        bool deferBuildValidationUntilAccept = ShouldDeferBuildValidationUntilAccept(stagedRecord);
        StagedDiffLaunchWorkflowResult result = new StagedDiffLaunchWorkflow().Launch(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            "AICodingServices.McpServer",
            diffToolPath,
            forceValidation,
            deferBuildValidationUntilAccept,
            verbose);
        return new AICodingServicesStagedDiffLaunchResult(
            result.StagedRecordSummary,
            result.StagedRecord,
            result.PreMergeValidation,
            result.CommandReductions,
            result.DiffLaunch,
            result.NextStep);
    }

    [McpServerTool]
    [Description("Run pre-merge validation, then launch the CodexUI browser staged review page for a staged edit record.")]
    public AICodingServicesStagedDiffLaunchResult LaunchStagedBrowserReview(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("CodexUI base URL used to build /review/staged/{stagedRecordId}. Defaults to http://localhost:5000.")] string browserReviewBaseUrl = "http://localhost:5000",
        [Description("Explicit browser executable path. When omitted, the system default browser handles the URL.")] string? browserPath = null,
        [Description("Force launch after an explicit human validation override.")] bool forceValidation = false,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        StagedEditRecord stagedRecord = workflowService.GetStagedRecord(stagedRecordId);
        bool deferBuildValidationUntilAccept = ShouldDeferBuildValidationUntilAccept(stagedRecord);
        StagedDiffLaunchWorkflowResult result = new StagedDiffLaunchWorkflow().Launch(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            "AICodingServices.McpServer",
            forceValidation: forceValidation,
            deferBuildValidationUntilAccept: deferBuildValidationUntilAccept,
            verbose: verbose,
            launchSurface: StagedReviewLaunchSurface.Browser,
            browserReviewBaseUrl: browserReviewBaseUrl,
            browserPath: browserPath);
        return new AICodingServicesStagedDiffLaunchResult(
            result.StagedRecordSummary,
            result.StagedRecord,
            result.PreMergeValidation,
            result.CommandReductions,
            result.DiffLaunch,
            result.NextStep);
    }

    [McpServerTool]
    [Description("Return the full persisted staged edit record by id. Use after compact stage/launch/decision replies when debug detail is needed.")]
    public StagedEditRecord GetStagedRecord(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId)
    {
        runtimeState.Touch();
        return workflowService.GetStagedRecord(stagedRecordId);
    }

    [McpServerTool]
    [Description("Create a proposed compare snapshot for a monitor Working file and return review paths.")]
    public CompareSnapshotResult CompareFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional compact ledger summary to append to the monitor-owned ledger.")] string? ledgerSummary = null,
        [Description("Refresh from source first if the Working copy is missing.")] bool refreshIfMissing = true,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        string path = ResolveWatchedPath(sourceFilePath);
        if (refreshIfMissing && !workflowService.GetStatus(path).WorkingFileExists)
        {
            workflowService.Refresh(path);
        }

        CompareSnapshotResult result = workflowService.Compare(path, ledgerSummary);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "compare-file", result.WorkingFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("List monitor run/history entries recorded under monitor-owned workflow history.")]
    public IReadOnlyList<Dictionary<string, object?>> ListMonitorRuns(
        [Description("Maximum entries to return.")] int maxEntries = 100)
    {
        runtimeState.Touch();
        string path = Path.Combine(workflowPaths.HistoryRoot, "_runs.json");
        if (!File.Exists(path))
        {
            return [];
        }

        IReadOnlyList<Dictionary<string, object?>> entries = JsonSerializer.Deserialize<IReadOnlyList<Dictionary<string, object?>>>(File.ReadAllText(path), JsonOptions) ?? [];
        return entries.TakeLast(maxEntries).ToArray();
    }

    [McpServerTool]
    [Description("Return recorded entries for one monitor run id.")]
    public IReadOnlyList<Dictionary<string, object?>> GetMonitorRun(
        [Description("Run id from list_monitor_runs.")] string runId)
    {
        runtimeState.Touch();
        return ListMonitorRuns(500)
            .Where(entry => entry.TryGetValue("runId", out object? value) && string.Equals(value?.ToString(), runId, StringComparison.Ordinal))
            .ToArray();
    }

    [McpServerTool]
    [Description("List monitor-owned per-file ledgers.")]
    public IReadOnlyList<AICodingServicesLedgerInfo> ListLedgers(
        [Description("Maximum ledgers to return.")] int maxEntries = 100)
    {
        runtimeState.Touch();
        string root = Path.Combine(workflowPaths.HistoryRoot, "Ledgers");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.md")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(maxEntries)
                .Select(info => new AICodingServicesLedgerInfo(info.FullName, info.Length, info.LastWriteTimeUtc))
                .ToArray()
            : [];
    }

    [McpServerTool]
    [Description("Read one monitor-owned per-file ledger by source file or ledger path.")]
    public AICodingServicesLedgerReadResult GetLedger(
        [Description("Optional source file path, absolute or relative to the watched solution folder.")] string? sourceFilePath = null,
        [Description("Optional absolute ledger path under the ledger root.")] string? ledgerPath = null)
    {
        runtimeState.Touch();
        string root = Path.GetFullPath(Path.Combine(workflowPaths.HistoryRoot, "Ledgers"));
        string path = !string.IsNullOrWhiteSpace(ledgerPath)
            ? Path.GetFullPath(ledgerPath)
            : Path.Combine(root, $"{Sanitize(workflowPaths.GetRelativeWatchedPath(ResolveWatchedPath(sourceFilePath ?? throw new InvalidOperationException("sourceFilePath or ledgerPath is required."))).Replace(Path.DirectorySeparatorChar, '_'))}.md");
        string relativeLedgerPath = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relativeLedgerPath)
            || relativeLedgerPath.Equals("..", StringComparison.Ordinal)
            || relativeLedgerPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativeLedgerPath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ledger path must be under monitor-owned ledger storage.");
        }

        return new AICodingServicesLedgerReadResult(path, File.Exists(path), File.Exists(path) ? File.ReadAllText(path) : string.Empty);
    }

    [McpServerTool]
    [Description("Archive/prune monitor-owned history. AICodingServices keeps history by default; this compatibility tool reports the current retention posture without deleting files.")]
    public AICodingServicesCompatibilityResult PruneMonitorHistory(
        [Description("Retention window in days.")] int retentionDays = 7)
    {
        runtimeState.Touch();
        return new AICodingServicesCompatibilityResult(
            "not-pruned",
            "AICodingServices currently keeps workflow history until an explicit UI/operator cleanup flow is implemented.",
            new Dictionary<string, string?> { ["retentionDays"] = retentionDays.ToString() });
    }

    [McpServerTool]
    [Description("Return the Markdown tool manifest for the AICodingServices MCP Server tool surface.")]
    public string GetToolManifest()
    {
        runtimeState.Touch();
        return ComposeToolManifest();
    }

    [McpServerTool]
    [Description("Return the normal staging guide for AICodingServices watched-project edits.")]
    public string GetStagingGuide()
    {
        runtimeState.Touch();
        return ComposeStagingGuide();
    }

    [McpServerTool]
    [Description("Return the smoke-test coverage todo/catalog for AICodingServices.")]
    public string GetSmokeTestCatalog()
    {
        runtimeState.Touch();
        string path = Path.Combine(settings.RepositoryRoot, "docs", "findings", "SmokeCoverageTodo.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "AICodingServices smoke coverage catalog is missing.";
    }

    [McpServerTool]
    [Description("List watched project folders. AICodingServices currently has one configured watched project folder.")]
    public IReadOnlyList<AICodingServicesWatchedProjectInfo> ListWatchedProjects()
    {
        runtimeState.Touch();
        return
        [
            new AICodingServicesWatchedProjectInfo(
                Path.GetFileName(settings.WatchedProjectFolder),
                settings.WatchedProjectFolder,
                File.Exists(settings.WatchedSolutionPath) ? [settings.WatchedSolutionPath] : [])
        ];
    }

    [McpServerTool]
    [Description("Request graceful shutdown of this AICodingServices MCP server process.")]
    public AICodingServicesServerShutdownResult ShutdownServer(
        [Description("Optional operator/client reason for the shutdown request.")] string? reason = null)
    {
        runtimeState.RequestShutdown(reason);
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            applicationLifetime.StopApplication();
        });
        return new AICodingServicesServerShutdownResult(Environment.ProcessId, DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(reason) ? "shutdown_server requested" : reason);
    }

    private string ComposeToolManifest()
    {
        StringBuilder builder = new();
        builder.AppendLine("# AICodingServices MCP Tool Manifest");
        builder.AppendLine();
        builder.AppendLine("This manifest is generated from the currently loaded AICodingServices MCP tool methods and includes edit-family policy guidance.");
        builder.AppendLine();
        foreach (MethodInfo method in typeof(AICodingServicesTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(method => ToToolName(method.Name), StringComparer.Ordinal))
        {
            string toolName = ToToolName(method.Name);
            string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description.";
            builder.AppendLine($"## `{toolName}`");
            builder.AppendLine();
            builder.AppendLine(description);
            builder.AppendLine();
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                builder.AppendLine("Parameters:");
                foreach (ParameterInfo parameter in parameters)
                {
                    string parameterDescription = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                    string nullable = IsNullableParameter(parameter) ? "optional" : "required";
                    builder.AppendLine($"- `{parameter.Name}` ({parameter.ParameterType.Name}, {nullable}): {parameterDescription}");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("## Safety Notes");
        builder.AppendLine();
        builder.AppendLine("- Watched source is not edited directly by agents.");
        builder.AppendLine("- Existing files enter through `refresh_file`; future files enter through `new_file`.");
        builder.AppendLine("- MCP edit sessions start with `start_monitor_session` and a non-empty `filesPlanned` list that declares per-file intent.");
        builder.AppendLine("- Candidate edits happen in monitor-owned Working files and must use the edit family allowed by the derived session policy.");
        builder.AppendLine("- Review uses `stage_candidate_for_review`, `launch_staged_diff`, browser staged review by default, and recorded accept/reject decisions. WinMerge remains available as an explicit fallback.");
        builder.AppendLine("- Planned sessions require all planned files to be staged before review launch.");
        builder.AppendLine("- For C# method replacement/body/symbol work, prefer Roslyn symbol tools; use span only with a reasoned fallback, and avoid text or whole-file replacement.");
        builder.AppendLine("- Accepted decisions trigger index refresh metadata after the planned session reaches terminal decisions; refresh before editing the same watched file again.");
        return builder.ToString();
    }

    private string ComposeStagingGuide()
    {
        StringBuilder builder = new();
        builder.AppendLine("# AICodingServices Staging Guide");
        builder.AppendLine();
        builder.AppendLine("Use this sequence for watched-project edits through MCP.");
        builder.AppendLine();
        builder.AppendLine("1. Check `get_self_check`, `get_workflow_status`, and `get_monitor_status` when starting a session.");
        builder.AppendLine("2. Call `start_monitor_session(filesPlanned: [...])` before editing. Include every watched file the session intends to mutate, even for one-file edits, and include `owningProjectPath` when the index cannot prove a single owner.");
        builder.AppendLine("3. Pass that same `sessionId` to `refresh_file`, `new_file`, every mutation tool, and `stage_candidate_for_review`.");
        builder.AppendLine("4. For existing files, call `refresh_file`. For future watched files, call `new_file`.");
        builder.AppendLine("5. Edit only the monitor-owned Working candidate with the derived legal edit family: Roslyn symbol tools for C# symbols, bounded span/text for markup/config, and whole-file only for new files or reasoned fallbacks.");
        builder.AppendLine("6. Stage every planned file with `stage_candidate_for_review(path, sessionId)`.");
        builder.AppendLine("7. Launch review with `launch_staged_diff` for every planned staged record before recording decisions; planned sessions require the full staged file set before browser review opens.");
        builder.AppendLine("8. The operator accepts or rejects every planned file in the staged browser review. Browser review is the default watched-source mutation surface; WinMerge is an explicit fallback.");
        builder.AppendLine("9. Record each result with `record_diff_decision`.");
        builder.AppendLine("10. After the last planned file reaches a terminal decision, inspect `indexRefresh` and call `refresh_file` before editing any accepted watched file again.");
        builder.AppendLine();
        builder.AppendLine("Failure paths:");
        builder.AppendLine();
        builder.AppendLine("- `blocked`, `dirty-unexpected`, `superseded`, missing Working files, and stale hashes require recovery before follow-up edits.");
        builder.AppendLine("- Planned-session launch checks staged overlay readiness; the full build/index validation runs at the terminal planned accept, not once per staged file.");
        builder.AppendLine("- Do not manually copy candidates into watched source outside staged review decision classification.");
        return builder.ToString();
    }

    private AICodingServicesGuardrailCheck[] BuildSelfCheckGuardrails()
    {
        string repositoryRoot = Path.GetFullPath(settings.RepositoryRoot);
        string runtimeRoot = Path.GetFullPath(settings.RuntimeRoot);
        string watchedProjectFolder = Path.GetFullPath(settings.WatchedProjectFolder);
        string workingRoot = Path.GetFullPath(workflowPaths.WorkingRoot);
        string historyRoot = Path.GetFullPath(workflowPaths.HistoryRoot);
        string stagedRoot = Path.GetFullPath(workflowPaths.StagedRoot);
        List<AICodingServicesGuardrailCheck> checks =
        [
            CheckPathExists("repository-root-exists", repositoryRoot, Directory.Exists(repositoryRoot), "Repository root exists.", "Repository root is missing."),
            CheckPathExists("watched-solution-exists", settings.WatchedSolutionPath, File.Exists(settings.WatchedSolutionPath), "Watched solution/project exists.", "Watched solution/project is missing."),
            CheckPathExists("watched-project-folder-exists", watchedProjectFolder, Directory.Exists(watchedProjectFolder), "Watched project folder exists.", "Watched project folder is missing."),
            CheckPathExists("runtime-root-exists", runtimeRoot, Directory.Exists(runtimeRoot), "Runtime root exists.", "Runtime root is missing."),
            CheckPathUnderRoot("working-under-runtime", workingRoot, runtimeRoot, "Working root is under runtime root.", "Working root is outside runtime root."),
            CheckPathUnderRoot("history-under-runtime", historyRoot, runtimeRoot, "History root is under runtime root.", "History root is outside runtime root."),
            CheckPathUnderRoot("staged-under-runtime", stagedRoot, runtimeRoot, "Staged root is under runtime root.", "Staged root is outside runtime root."),
            CheckPathOutsideRoot("runtime-outside-watched-source", runtimeRoot, watchedProjectFolder, "Runtime state is outside watched source.", "Runtime state is inside watched source."),
        ];

        string? diffTool = settings.WinMergeCandidatePaths.FirstOrDefault(File.Exists);
        checks.Add(diffTool is null
            ? new AICodingServicesGuardrailCheck("diff-tool-available", "warning", "No configured WinMerge candidate exists on disk.", string.Join(";", settings.WinMergeCandidatePaths))
            : new AICodingServicesGuardrailCheck("diff-tool-available", "passed", "Configured WinMerge candidate exists.", diffTool));

        return checks.ToArray();
    }

    private static AICodingServicesGuardrailCheck CheckPathExists(string name, string path, bool passed, string passedMessage, string failedMessage)
    {
        return new AICodingServicesGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static AICodingServicesGuardrailCheck CheckPathUnderRoot(string name, string path, string root, string passedMessage, string failedMessage)
    {
        bool passed = IsPathUnderRoot(path, root);
        return new AICodingServicesGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static AICodingServicesGuardrailCheck CheckPathOutsideRoot(string name, string path, string root, string passedMessage, string failedMessage)
    {
        bool passed = !IsPathUnderRoot(path, root);
        return new AICodingServicesGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNullableParameter(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue)
        {
            return true;
        }

        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        if (!parameter.ParameterType.IsValueType)
        {
            NullabilityInfo nullabilityInfo = new NullabilityInfoContext().Create(parameter);
            return nullabilityInfo.ReadState == NullabilityState.Nullable;
        }

        return false;
    }

    private static string ToToolName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private string SessionRoot => Path.Combine(MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings), "workflow", "sessions");

    private string ResolveWatchedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        string fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(settings.WatchedProjectFolder, path));
        workflowPaths.GetRelativeWatchedPath(fullPath);
        return fullPath;
    }

    private EditSessionStatus EnsureSession(string watchedFilePath)
    {
        return workflowService.EnsureEditableSession(watchedFilePath);
    }

    private void SaveSession(AICodingServicesSessionState session)
    {
        Directory.CreateDirectory(SessionRoot);
        File.WriteAllText(GetSessionPath(session.SessionId), JsonSerializer.Serialize(session, JsonOptions));
    }

    private AICodingServicesSessionFileAccess RecordSessionFileAccess(
        string sessionId,
        string sourceFilePath,
        string accessKind,
        AICodingServicesFileHashInfo hash)
    {
        AICodingServicesSessionState session = LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
        List<AICodingServicesSessionFileAccess> files = session.Files.ToList();
        AICodingServicesSessionFileAccess? previous = files
            .FirstOrDefault(item => item.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase));
        AICodingServicesSessionFileAccess updated = new(
            sessionId,
            sourceFilePath,
            workflowPaths.GetRelativeWatchedPath(sourceFilePath),
            accessKind,
            hash,
            (previous?.FetchCount ?? 0) + 1,
            previous?.FirstAccessedAtUtc ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        if (previous is not null)
        {
            files.Remove(previous);
        }

        files.Add(updated);
        SaveSession(session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Files = files
        });
        return updated;
    }

    private AICodingServicesSessionState? LoadSessionById(string sessionId)
    {
        string path = GetSessionPath(sessionId);
        return File.Exists(path) ? LoadSession(path) : null;
    }

    private AICodingServicesSessionState? LoadSession(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AICodingServicesSessionState>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string GetSessionPath(string sessionId)
    {
        return Path.Combine(SessionRoot, $"{Sanitize(sessionId)}.json");
    }

    private void RecordRoslynSessionEvent(string? sessionId, string eventType, RoslynEditResult result)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, eventType, result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    private PlannedSessionDecisionOptions BuildPlannedSessionDecisionOptions(string stagedRecordId, string requestedDecision)
    {
        StagedEditRecord currentRecord = workflowService.GetStagedRecord(stagedRecordId);
        if (string.IsNullOrWhiteSpace(currentRecord.SessionId))
        {
            return new PlannedSessionDecisionOptions(false, null, []);
        }

        AICodingServicesSessionEditPlan? editPlan = LoadSessionById(currentRecord.SessionId)?.EditPlan;
        if (editPlan is null || editPlan.FilesPlanned.Count == 0)
        {
            return new PlannedSessionDecisionOptions(false, null, []);
        }

        IReadOnlyList<StagedEditRecord> sessionRecords = workflowService.ListStagedRecords(currentRecord.SessionId);
        HashSet<string> terminalPlannedPaths = sessionRecords
            .Where(record => !record.StagedRecordId.Equals(stagedRecordId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(record.Decision))
            .Select(record => Path.GetFullPath(record.WatchedFilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        terminalPlannedPaths.Add(Path.GetFullPath(currentRecord.WatchedFilePath));
        bool allPlannedFilesDecided = editPlan.FilesPlanned.All(file =>
            terminalPlannedPaths.Contains(Path.GetFullPath(file.SourceFilePath)));

        string normalizedDecision = requestedDecision.Trim().ToLowerInvariant();
        HashSet<string> acceptedPaths = sessionRecords
            .Where(record => !record.StagedRecordId.Equals(stagedRecordId, StringComparison.Ordinal)
                && record.Classification is "accepted" or "accepted-normalized")
            .Select(record => Path.GetFullPath(record.WatchedFilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedDecision.Equals("accepted", StringComparison.OrdinalIgnoreCase))
        {
            acceptedPaths.Add(Path.GetFullPath(currentRecord.WatchedFilePath));
        }

        AICodingServicesSessionPlannedFile[] acceptedPlannedFiles = editPlan.FilesPlanned
            .Where(file => acceptedPaths.Contains(Path.GetFullPath(file.SourceFilePath)))
            .ToArray();
        if (acceptedPlannedFiles.Length == 0)
        {
            return new PlannedSessionDecisionOptions(false, null, []);
        }

        PostAcceptIndexRefreshPlan refreshPlan = new()
        {
            ChangedFilePaths = acceptedPlannedFiles.Select(file => file.SourceFilePath).ToArray(),
            OwningProjectPaths = acceptedPlannedFiles.Select(file => file.OwningProjectPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        StagedEditRecord[] terminalValidationRecords = !allPlannedFilesDecided
            ? []
            : sessionRecords
                .Append(currentRecord)
                .Where(record => acceptedPaths.Contains(Path.GetFullPath(record.WatchedFilePath)))
                .GroupBy(record => Path.GetFullPath(record.WatchedFilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(record => record.CreatedAtUtc, StringComparer.Ordinal).First())
                .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        return new PlannedSessionDecisionOptions(!allPlannedFilesDecided, refreshPlan, terminalValidationRecords);
    }

    private bool ShouldDeferPlannedOverlayValidation(string? sessionId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        AICodingServicesSessionEditPlan? editPlan = RequireSessionEditPlan(sessionId);
        EnsurePlannedFile(editPlan, sourceFilePath);
        return true;
    }

    private void EnsurePlannedMutationAllowed(
        string? sessionId,
        string sourceFilePath,
        SessionEditOperationFamily operationFamily,
        string? manifestJson = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session edit scope is required before MCP workflow mutations. Call start_monitor_session with filesPlanned before editing or staging.");
        }

        AICodingServicesSessionEditPlan editPlan = RequireSessionEditPlan(sessionId);
        AICodingServicesSessionPlannedFile plannedFile = EnsurePlannedFile(editPlan, sourceFilePath);
        SessionDerivedEditPolicy? policy = plannedFile.DerivedPolicy;
        if (policy is null)
        {
            return;
        }

        SessionEditPolicyDecision decision = sessionIntentPolicyService.Evaluate(policy, operationFamily, ExtractFallbackReason(manifestJson));
        if (!decision.Allowed)
        {
            throw new InvalidOperationException(decision.Message);
        }
    }

    private void EnsurePlannedMutationAllowed(string? sessionId, string sourceFilePath)
    {
        EnsurePlannedMutationAllowed(sessionId, sourceFilePath, SessionEditOperationFamily.Unknown);
    }

    private bool ShouldDeferBuildValidationUntilAccept(StagedEditRecord stagedRecord)
    {
        if (string.IsNullOrWhiteSpace(stagedRecord.SessionId))
        {
            return false;
        }

        AICodingServicesSessionEditPlan editPlan = RequireSessionEditPlan(stagedRecord.SessionId);
        EnsurePlannedFile(editPlan, stagedRecord.WatchedFilePath);
        IReadOnlyList<StagedEditRecord> sessionRecords = workflowService.ListStagedRecords(stagedRecord.SessionId);
        foreach (AICodingServicesSessionPlannedFile plannedFile in editPlan.FilesPlanned)
        {
            string plannedPath = Path.GetFullPath(plannedFile.SourceFilePath);
            IEnumerable<StagedEditRecord> plannedFileRecords = sessionRecords.Where(record =>
                Path.GetFullPath(record.WatchedFilePath).Equals(plannedPath, StringComparison.OrdinalIgnoreCase));
            // Launch-deadlock fix: a planned file already carrying a final decision is
            // satisfied. Interleaving launch -> decide -> launch on the remaining files
            // must not throw just because an earlier file was decided and no longer has an
            // active (undecided) staged record. Only files NOT yet decided must still have
            // an active staged record.
            bool alreadyDecided = plannedFileRecords.Any(record => !string.IsNullOrWhiteSpace(record.Decision));
            if (alreadyDecided)
            {
                continue;
            }

            bool hasActiveStagedRecord = plannedFileRecords.Any(record =>
                string.IsNullOrWhiteSpace(record.Decision)
                && string.IsNullOrWhiteSpace(record.SupersededByStagedRecordId)
                && !record.Status.Equals("superseded", StringComparison.OrdinalIgnoreCase)
                && !record.Classification.Equals("superseded", StringComparison.OrdinalIgnoreCase));
            if (!hasActiveStagedRecord)
            {
                throw new InvalidOperationException("Cannot launch review until every planned session edit file has a staged record. Stage missing planned file: " + plannedFile.RelativePath);
            }
        }

        return true;
    }

    private AICodingServicesSessionEditPlan RequireSessionEditPlan(string sessionId)
    {
        AICodingServicesSessionState session = GetMonitorSession(sessionId);
        if (session.EditPlan is null || session.EditPlan.FilesPlanned.Count == 0)
        {
            throw new InvalidOperationException("Session edit plan is required before MCP workflow edits. Call start_monitor_session with filesPlanned before editing, staging, or launching review.");
        }

        return session.EditPlan;
    }

    private IReadOnlyList<AICodingServicesSessionPlannedFile> BuildPlannedFiles(IReadOnlyList<AICodingServicesSessionPlannedFileInput> filesPlanned)
    {
        if (filesPlanned.Count == 0)
        {
            throw new InvalidOperationException("At least one planned edit file is required.");
        }

        List<AICodingServicesSessionPlannedFile> plannedFiles = [];
        foreach (AICodingServicesSessionPlannedFileInput input in filesPlanned)
        {
            string sourceFilePath = ResolveWatchedPath(input.SourceFilePath);
            string owningProjectPath = string.IsNullOrWhiteSpace(input.OwningProjectPath)
                ? ResolveOwningProjectPath(sourceFilePath)
                : Path.GetFullPath(input.OwningProjectPath);
            if (plannedFiles.Any(file =>
                file.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase)
                && file.OwningProjectPath.Equals(owningProjectPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            SessionPlannedFileIntent declaredIntent = BuildPlannedFileIntent(input, sourceFilePath);
            SessionDerivedEditPolicy derivedPolicy = sessionIntentPolicyService.DerivePolicy(declaredIntent);
            plannedFiles.Add(new AICodingServicesSessionPlannedFile(
                sourceFilePath,
                workflowPaths.GetRelativeWatchedPath(sourceFilePath),
                owningProjectPath,
                Path.GetFileName(sourceFilePath),
                Path.GetFileName(owningProjectPath),
                string.IsNullOrWhiteSpace(input.Role) ? "edit" : input.Role,
                input.Reason ?? string.Empty,
                declaredIntent,
                derivedPolicy));
        }

        return plannedFiles;
    }

    private static SessionPlannedFileIntent BuildPlannedFileIntent(AICodingServicesSessionPlannedFileInput input, string sourceFilePath)
    {
        string targetKind = string.IsNullOrWhiteSpace(input.TargetKind)
            ? InferTargetKind(sourceFilePath)
            : input.TargetKind;
        string changeKind = string.IsNullOrWhiteSpace(input.ChangeKind)
            ? "Unknown"
            : input.ChangeKind;
        string expectedShape = string.IsNullOrWhiteSpace(input.ExpectedShape)
            ? "Unknown"
            : input.ExpectedShape;

        return new SessionPlannedFileIntent(
            targetKind,
            changeKind,
            expectedShape,
            input.TargetSymbols ?? [],
            string.IsNullOrWhiteSpace(input.Risk) ? "Unknown" : input.Risk,
            input.DiscoveryAlreadyDone);
    }

    private static string InferTargetKind(string sourceFilePath)
    {
        string extension = Path.GetExtension(sourceFilePath);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SessionIntentTargetKind.CSharpSource);
        }

        if (extension.Equals(".razor", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SessionIntentTargetKind.RazorMarkup);
        }

        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SessionIntentTargetKind.Markdown);
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SessionIntentTargetKind.Json);
        }

        if (extension.Equals(".css", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SessionIntentTargetKind.Css);
        }

        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SessionIntentTargetKind.Markup);
        }

        return nameof(SessionIntentTargetKind.Unknown);
    }

    private static string? ExtractFallbackReason(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(manifestJson);
            if (document.RootElement.TryGetProperty("fallbackReason", out JsonElement fallbackReason)
                && fallbackReason.ValueKind == JsonValueKind.String)
            {
                return fallbackReason.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static AICodingServicesSessionPlannedFile EnsurePlannedFile(AICodingServicesSessionEditPlan editPlan, string sourceFilePath)
    {
        string fullPath = Path.GetFullPath(sourceFilePath);
        AICodingServicesSessionPlannedFile? plannedFile = editPlan.FilesPlanned.FirstOrDefault(file =>
            Path.GetFullPath(file.SourceFilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (plannedFile is null)
        {
            throw new InvalidOperationException("Source file is not in the session edit plan: " + fullPath);
        }

        return plannedFile;
    }

    private string ResolveOwningProjectPath(string sourceFilePath)
    {
        string normalizedSourceFilePath = Path.GetFullPath(sourceFilePath);
        IndexedDocumentRow[] matches = queryService.ListDocuments(filePath: normalizedSourceFilePath)
            .Where(document => Path.GetFullPath(document.FilePath).Equals(normalizedSourceFilePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("Planned file must include owningProjectPath when the existing index does not have exactly one owning project for: " + sourceFilePath);
        }

        return Path.GetFullPath(matches[0].ProjectPath);
    }

    private static AICodingServicesFileHashInfo GetFileHashInfo(string path)
    {
        FileInfo info = new(path);
        return new AICodingServicesFileHashInfo(
            ComputeFileHash(path),
            info.Length,
            info.LastWriteTimeUtc);
    }

    private static AICodingServicesToolErrorResult? TryCreateIndexedStableSymbolKeyError(string stableSymbolKey)
    {
        if (string.IsNullOrWhiteSpace(stableSymbolKey))
        {
            return new AICodingServicesToolErrorResult(
                true,
                "A stable indexed symbol key is required. Use query_solution_index, find_indexed_symbols, or get_indexed_symbol to obtain a symbol:<hash> key.",
                "symbol:<hash>",
                stableSymbolKey);
        }

        if (stableSymbolKey.StartsWith("symbol:", StringComparison.Ordinal))
        {
            return null;
        }

        if (stableSymbolKey.Contains("::", StringComparison.Ordinal))
        {
            return new AICodingServicesToolErrorResult(
                true,
                "This looks like a Roslyn source-map selector key, not an indexed symbol key. find_indexed_references and find_indexed_callers require the symbol:<hash> key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.",
                "symbol:<hash>",
                stableSymbolKey);
        }

        return new AICodingServicesToolErrorResult(
            true,
            "Indexed reference tools require a stable indexed symbol key in symbol:<hash> form. Use query_solution_index, find_indexed_symbols, or get_indexed_symbol first.",
            "symbol:<hash>",
            stableSymbolKey);
    }

    private static AICodingServicesIndexedReferenceResult ToMcpReferenceRow(IndexedReferenceRow reference)
    {
        return new AICodingServicesIndexedReferenceResult(
            reference.TargetStableKey,
            reference.FilePath,
            reference.Line,
            reference.Column,
            reference.ReferenceKind,
            reference.Snippet,
            reference.TargetName,
            reference.TargetKind,
            reference.CallerStableKey,
            reference.CallerName,
            reference.CallerKind);
    }

    private static bool IsRecoverableRoslynGuidanceError(InvalidOperationException ex)
    {
        return ex.Message.Contains("Razor markup", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("supports C# source files only", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedToolFailure(Exception ex)
    {
        return ex is InvalidOperationException
            || ex is IOException
            || ex is FileNotFoundException
            || ex is UnauthorizedAccessException;
    }

    private static AICodingServicesToolErrorResult CreateToolError(Exception ex, string expected, string? received = null)
    {
        return new AICodingServicesToolErrorResult(true, ex.Message, expected, received ?? ex.GetType().Name);
    }

    private static void ValidateExpectedHash(string path, string? expectedFileHash)
    {
        if (!string.IsNullOrWhiteSpace(expectedFileHash)
            && !ComputeFileHash(path).Equals(expectedFileHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Working candidate hash did not match expectedFileHash.");
        }
    }

    private static bool IsUnderBuildOrHiddenDirectory(string path)
    {
        string[] parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.StartsWith(".", StringComparison.Ordinal)
            || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeFileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "item" : clean;
    }

    private void LogStartupStep(CodingServicesStartupStep step, string repositoryRoot, string settingsPath)
    {
        logger.Write(
            MonitorLogLevel.Information,
            "AICodingServices.McpServer",
            "adapter.mcp.initialize_coding_services.step",
            "Initialize Coding Services emitted a startup step.",
            new Dictionary<string, string>
            {
                ["stepName"] = step.Name,
                ["stepDetail"] = step.Detail,
                ["repositoryRoot"] = repositoryRoot,
                ["settingsPath"] = settingsPath
            });
    }

    private sealed class AttachedMcpSessionHostController : ICodingServicesSessionHostController
    {
        public Task<CodingServicesHostCommandResult> ActivateNativeMountedServerAsync(string serverName, CancellationToken cancellationToken)
        {
            return Task.FromResult(CodingServicesHostCommandResult.Success(
                $"Initialize Coding Services is already executing through '{serverName}', so no additional host activation was required."));
        }

        public Task<CodingServicesNativeMountResult> GetNativeMountStatusAsync(string serverName, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CodingServicesNativeMountResult(
                CodingServicesNativeMountState.Visible,
                $"Initialize Coding Services is already executing through the live '{serverName}' MCP route."));
        }

        public Task<CodingServicesHostCommandResult> ResetThreadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(CodingServicesHostCommandResult.Unsupported(
                "Thread reset cannot be issued from inside the MCP server process. Reconnect/remount from the host if this session disappears."));
        }
    }
}

