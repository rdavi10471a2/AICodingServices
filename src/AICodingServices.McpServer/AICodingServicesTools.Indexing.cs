using AICodingServices.Core;
using AICodingServices.Data;
using AICodingServices.Indexing;
using AICodingServices.Workflow;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace AICodingServices.McpServer;

public sealed partial class AICodingServicesTools
{
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
}
