using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectEditor;

[FileVersion("1.4")]
[AIFileContext(
    "Services/CteSelectionSessionEditor.cs",
    "Selectable CTE graph session used by the domain object editor candidate page.",
    Responsibilities = "Apply construction-by-subtraction selection changes, preserve required dependency fields, keep the root object anchored, and expose render-ready row models.",
    Nuances = "This is the graph rewrite engine over the parser model. Razor pages should render controls and dispatch commands, not duplicate these selection rules.")]
public sealed class CteSelectionSessionEditor
{
    private readonly CteFieldParser parser;
    private readonly HashSet<string> selectedFieldKeys = new(StringComparer.OrdinalIgnoreCase);

    private bool resultSqlDirty = true;
    private bool resultCteSqlDirty = true;
    private bool rootAnchorNoticeActive;
    private string resultSql = string.Empty;
    private string resultCteSql = string.Empty;

    public CteSelectionSessionEditor(CteFieldParser parser)
    {
        this.parser = parser;
    }

    public CteParseResult? ParseResult { get; private set; }

    public int RenderGeneration { get; private set; }

    public int OutputGeneration { get; private set; }

    public IReadOnlySet<string> SelectedFieldKeys => selectedFieldKeys;

    public IReadOnlySet<string> RequiredFieldKeys { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TrimmedCteDefinition> TrimmedCtePanels { get; private set; } =
        Array.Empty<TrimmedCteDefinition>();

    public string OutlineText { get; private set; } = string.Empty;

    public string StatusMessage { get; private set; } = string.Empty;

    public string WhereDependencyNotice { get; private set; } = string.Empty;

    public string? InspectedObjectName { get; private set; }

    public bool HasRequiredDependencyFields => RequiredFieldKeys.Count > 0;

    public bool IsPlainSelectWrapped => ParseResult?.WasPlainSelectWrapped == true;

    public IReadOnlyList<CteDefinition> DomainObjects => ParseResult is null
        ? Array.Empty<CteDefinition>()
        : ParseResult.Ctes;

    public TrimmedCteDefinition? InspectedCte => string.IsNullOrWhiteSpace(InspectedObjectName)
        ? null
        : TrimmedCtePanels.FirstOrDefault(cte =>
            string.Equals(cte.Name, InspectedObjectName, StringComparison.OrdinalIgnoreCase));

    public string RequiredPathNotice
    {
        get
        {
            if (ParseResult?.FinalQuery.Sources.FirstOrDefault() is not { } rootSource)
            {
                return string.Empty;
            }

            var rootState = GetSourceRowState(rootSource);
            return rootState is { IsFullySelected: false, IsOnActivePath: true }
                ? "Outer key is required for the selected object path; removing it would break the generated SQL."
                : string.Empty;
        }
    }

    public void LoadSql(string sql)
    {
        RenderGeneration++;
        InspectedObjectName = null;
        ParseResult = parser.Parse(sql);
        selectedFieldKeys.Clear();
        rootAnchorNoticeActive = false;
        RequiredFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StatusMessage = ParseResult.WasPlainSelectWrapped
            ? "Plain SELECT is being shaped as SourceObject."
            : string.Empty;

        if (ParseResult.IsSuccess)
        {
            foreach (var field in DomainObjects.SelectMany(cte => cte.Fields))
            {
                selectedFieldKeys.Add(field.Key);
            }
        }

        RefreshOutputs();
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
    }

    public void ClearForInputBoundary(string message)
    {
        RenderGeneration++;
        ParseResult = null;
        selectedFieldKeys.Clear();
        rootAnchorNoticeActive = false;
        RequiredFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        InspectedObjectName = null;
        StatusMessage = message;
        RefreshOutputs();
    }

    public void ToggleField(string? key, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(key) || selectedFieldKeys.Contains(key) == isSelected)
        {
            return;
        }

        if (isSelected)
        {
            selectedFieldKeys.Add(key);
        }
        else
        {
            selectedFieldKeys.Remove(key);
        }

        EnsureRootAnchor();
        RefreshOutputs();
        RefreshInspectedCte();
        RefreshStatus();
    }

    public void SetPath(CtePathRowEditor row, bool isSelected)
    {
        if (row.Source is not null)
        {
            ToggleSource(row.Source, isSelected);
            return;
        }

        if (row.Join is not null)
        {
            ToggleJoin(row.Join, isSelected);
        }
    }

    public void TogglePathSelection(CtePathRowEditor row)
    {
        SetPath(row, !row.HasAllSelectedFields);
    }

    public void InspectPath(CtePathRowEditor row)
    {
        InspectObject(row.ObjectName);
    }

    public void InspectObject(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return;
        }

        InspectedObjectName = TrimmedCtePanels.Any(cte =>
            string.Equals(cte.Name, objectName, StringComparison.OrdinalIgnoreCase))
                ? objectName
                : null;
    }

    public void CloseInspector()
    {
        InspectedObjectName = null;
    }

    public string EnsureResultSql(string tab)
    {
        if (ParseResult is null)
        {
            return string.Empty;
        }

        if (tab == "result")
        {
            if (resultSqlDirty)
            {
                resultSql = parser.GenerateTrimmedDerivedTableSql(ParseResult, selectedFieldKeys);
                resultSqlDirty = false;
            }

            return resultSql;
        }

        if (resultCteSqlDirty)
        {
            resultCteSql = parser.GenerateTrimmedSql(ParseResult, selectedFieldKeys);
            resultCteSqlDirty = false;
        }

        return resultCteSql;
    }

    public IReadOnlyList<CteOutputFieldRowEditor> GetOutputRows()
    {
        return DomainObjects
            .SelectMany(cte => cte.Fields.Select(field => new CteOutputFieldRowEditor(
                field.Key,
                $"{cte.Name}.{field.Name}",
                GetFieldRowState(field.Key))))
            .ToList();
    }

    public IReadOnlyList<CtePathRowEditor> GetPathRows()
    {
        if (ParseResult is null)
        {
            return Array.Empty<CtePathRowEditor>();
        }

        var rows = new List<CtePathRowEditor>();
        if (ParseResult.FinalQuery.Sources.FirstOrDefault() is { } rootSource)
        {
            rows.Add(CreatePathRow(rootSource, isRoot: true));
        }

        rows.AddRange(ParseResult.FinalQuery.Joins.Select(CreatePathRow));
        return rows;
    }

    public CteSelectionEditorStateSnapshot GetEngineStateSnapshot()
    {
        if (ParseResult is null)
        {
            return new CteSelectionEditorStateSnapshot([], [], [], [], [], []);
        }

        var pathRows = GetPathRows();
        return new CteSelectionEditorStateSnapshot(
            selectedFieldKeys
                .Select(DisplayForFieldKey)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RequiredFieldKeys
                .Select(DisplayForFieldKey)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            pathRows
                .Where(row => row.State.IsActive)
                .Select(row => row.ObjectName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            pathRows
                .Where(row => row.HasAllSelectedFields)
                .Select(row => row.ObjectName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            GetRootAnchorKeys()
                .Select(DisplayForFieldKey)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TrimmedCtePanels
                .Where(cte => cte.IsKept)
                .Select(cte => cte.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private void ToggleSource(SourceRef source, bool isSelected)
    {
        var cte = ResolveObjectForSource(source);
        if (cte is null)
        {
            return;
        }

        if (isSelected && SourceHasAllSelectedFields(source))
        {
            return;
        }

        if (!isSelected && !SourceHasSelectedFields(source))
        {
            return;
        }

        ToggleCteFields(cte, isSelected);
        RefreshInspectedCte();
    }

    private void ToggleJoin(JoinRef join, bool isSelected)
    {
        var source = ParseResult?.FinalQuery.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, join.RightAlias, StringComparison.OrdinalIgnoreCase));
        if (source is not null)
        {
            ToggleSource(source, isSelected);
        }
    }

    private void ToggleCteFields(CteDefinition cte, bool isSelected)
    {
        var root = ParseResult?.FinalQuery.Sources.FirstOrDefault();
        var isRootCte = root is not null
            && string.Equals(root.ObjectName, cte.Name, StringComparison.OrdinalIgnoreCase);

        foreach (var field in cte.Fields)
        {
            if (isSelected)
            {
                selectedFieldKeys.Add(field.Key);
            }
            else
            {
                selectedFieldKeys.Remove(field.Key);
            }
        }

        if (!isSelected && isRootCte)
        {
            SetRootAnchorNotice();
        }
        else
        {
            rootAnchorNoticeActive = false;
            StatusMessage = string.Empty;
        }

        EnsureRootAnchor();
        RefreshOutputs();
        RefreshStatus();
    }

    private void EnsureRootAnchor()
    {
        if (ParseResult is null || ParseResult.FinalQuery.Sources.FirstOrDefault() is not { } rootSource)
        {
            return;
        }

        var rootCte = ParseResult.Ctes.FirstOrDefault(cte =>
            string.Equals(cte.Name, rootSource.ObjectName, StringComparison.OrdinalIgnoreCase));
        if (rootCte is null || rootCte.Fields.Any(field => selectedFieldKeys.Contains(field.Key)))
        {
            return;
        }

        foreach (var key in GetRootAnchorKeys())
        {
            selectedFieldKeys.Add(key);
        }

        SetRootAnchorNotice();
    }

    private void SetRootAnchorNotice()
    {
        rootAnchorNoticeActive = true;
        StatusMessage = "The root object stays anchored so the new object remains valid.";
    }

    private IReadOnlyList<string> GetRootAnchorKeys()
    {
        if (ParseResult is null || ParseResult.FinalQuery.Sources.FirstOrDefault() is not { } rootSource)
        {
            return Array.Empty<string>();
        }

        var rootCte = ParseResult.Ctes.FirstOrDefault(cte =>
            string.Equals(cte.Name, rootSource.ObjectName, StringComparison.OrdinalIgnoreCase));
        if (rootCte is null)
        {
            return Array.Empty<string>();
        }

        var activeJoins = ParseResult.FinalQuery.Joins
            .Where(IsJoinConnectedToSelectedSource)
            .ToList();
        var anchors = activeJoins
            .SelectMany(join => join.ColumnRefs)
            .Where(reference => string.Equals(reference.Alias, rootSource.Alias, StringComparison.OrdinalIgnoreCase))
            .Select(reference => rootCte.Fields.FirstOrDefault(field =>
                string.Equals(field.Name, reference.Column, StringComparison.OrdinalIgnoreCase)))
            .Where(field => field is not null)
            .Select(field => field!.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (anchors.Count > 0)
        {
            return anchors;
        }

        return rootCte.Fields.FirstOrDefault() is { } fallback
            ? new[] { fallback.Key }
            : Array.Empty<string>();
    }

    private void RefreshOutputs()
    {
        if (ParseResult is null)
        {
            OutlineText = string.Empty;
            resultSql = string.Empty;
            resultCteSql = string.Empty;
            resultSqlDirty = true;
            resultCteSqlDirty = true;
            TrimmedCtePanels = Array.Empty<TrimmedCteDefinition>();
            RequiredFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            WhereDependencyNotice = string.Empty;
            OutputGeneration++;
            return;
        }

        OutlineText = parser.Describe(ParseResult);
        resultSqlDirty = true;
        resultCteSqlDirty = true;
        var requiredKeys = parser.GetRequiredFieldKeys(ParseResult, selectedFieldKeys)
            .Where(key => !selectedFieldKeys.Contains(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (rootAnchorNoticeActive)
        {
            foreach (var key in GetRootAnchorKeys())
            {
                if (!selectedFieldKeys.Contains(key))
                {
                    requiredKeys.Add(key);
                }
            }
        }

        RequiredFieldKeys = requiredKeys;
        TrimmedCtePanels = parser.GenerateTrimmedCtes(ParseResult, selectedFieldKeys, RequiredFieldKeys);
        WhereDependencyNotice = BuildWhereDependencyNotice();
        OutputGeneration++;
        RefreshInspectedCte();
    }

    private string BuildWhereDependencyNotice()
    {
        var aliases = TrimmedCtePanels
            .SelectMany(cte => cte.Lines)
            .Where(line => line.State is TrimmedSqlLineState.RequiredByWhere or TrimmedSqlLineState.RequiredByJoinAndWhere)
            .Select(TryExtractJoinAlias)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return aliases.Count == 0
            ? string.Empty
            : $"WHERE keeps {string.Join(", ", aliases)} joins active.";
    }

    private static string TryExtractJoinAlias(TrimmedSqlLine line)
    {
        const string marker = " AS ";
        var text = line.Text;
        var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var aliasStart = markerIndex + marker.Length;
        var aliasEnd = text.IndexOf(' ', aliasStart);
        return aliasEnd > aliasStart
            ? text[aliasStart..aliasEnd]
            : text[aliasStart..].Trim();
    }

    private void RefreshInspectedCte()
    {
        if (!string.IsNullOrWhiteSpace(InspectedObjectName))
        {
            InspectObject(InspectedObjectName);
        }
    }

    private void RefreshStatus()
    {
        if (!rootAnchorNoticeActive || IsRootOnlyAnchored())
        {
            return;
        }

        rootAnchorNoticeActive = false;
        StatusMessage = string.Empty;
    }

    private bool IsRootOnlyAnchored()
    {
        if (ParseResult is null || ParseResult.FinalQuery.Sources.FirstOrDefault() is not { } rootSource)
        {
            return false;
        }

        var rootCte = ParseResult.Ctes.FirstOrDefault(cte =>
            string.Equals(cte.Name, rootSource.ObjectName, StringComparison.OrdinalIgnoreCase));
        if (rootCte is null)
        {
            return false;
        }

        var selectedRootKeys = rootCte.Fields
            .Where(field => selectedFieldKeys.Contains(field.Key))
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var anchorKeys = GetRootAnchorKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);

        return selectedRootKeys.Count > 0
            && selectedRootKeys.IsSubsetOf(anchorKeys);
    }

    private bool IsJoinConnectedToSelectedSource(JoinRef join)
    {
        if (ParseResult is null)
        {
            return false;
        }

        var source = ParseResult.FinalQuery.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, join.RightAlias, StringComparison.OrdinalIgnoreCase));
        return source is not null && SourceHasSelectedFields(source);
    }

    private bool SourceHasSelectedFields(SourceRef source)
    {
        var fields = ResolveObjectForSource(source)?.Fields;
        return fields?.Any(field => selectedFieldKeys.Contains(field.Key)) == true;
    }

    private bool SourceHasAllSelectedFields(SourceRef source)
    {
        var fields = ResolveObjectForSource(source)?.Fields;
        return fields is { Count: > 0 } && fields.All(field => selectedFieldKeys.Contains(field.Key));
    }

    private string DisplayForFieldKey(string key)
    {
        foreach (var cte in DomainObjects)
        {
            var field = cte.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                return $"{cte.Name}.{field.Name}";
            }
        }

        return string.Empty;
    }

    private QueryRowStateEditor GetFieldRowState(string? key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? QueryRowStateEditor.Inactive
            : new QueryRowStateEditor(
                selectedFieldKeys.Contains(key),
                selectedFieldKeys.Contains(key),
                RequiredFieldKeys.Contains(key));
    }

    private QueryRowStateEditor GetSourceRowState(SourceRef source)
    {
        var fields = ResolveObjectForSource(source)?.Fields;
        if (fields is null || fields.Count == 0)
        {
            return QueryRowStateEditor.Inactive;
        }

        return new QueryRowStateEditor(
            fields.Any(field => selectedFieldKeys.Contains(field.Key)),
            fields.All(field => selectedFieldKeys.Contains(field.Key)),
            fields.Any(field => RequiredFieldKeys.Contains(field.Key)));
    }

    private QueryRowStateEditor GetJoinRowState(JoinRef join)
    {
        var source = ParseResult?.FinalQuery.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, join.RightAlias, StringComparison.OrdinalIgnoreCase));
        return source is null ? QueryRowStateEditor.Inactive : GetSourceRowState(source);
    }

    private CteDefinition? ResolveObjectForSource(SourceRef source)
    {
        return ParseResult?.Ctes.FirstOrDefault(cte =>
            string.Equals(cte.Name, source.ObjectName, StringComparison.OrdinalIgnoreCase));
    }

    private CtePathRowEditor CreatePathRow(SourceRef source, bool isRoot)
    {
        var state = GetSourceRowState(source);
        return new CtePathRowEditor(
            $"source:{source.Alias}:{source.ObjectName}",
            source.ObjectName,
            source.DisplayText,
            state,
            SourceHasAllSelectedFields(source),
            isRoot,
            source,
            null);
    }

    private CtePathRowEditor CreatePathRow(JoinRef join)
    {
        var source = ParseResult?.FinalQuery.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, join.RightAlias, StringComparison.OrdinalIgnoreCase));
        var state = GetJoinRowState(join);
        return new CtePathRowEditor(
            $"join:{join.RightAlias}:{join.DisplayText}",
            source?.ObjectName ?? join.RightAlias,
            join.DisplayText,
            state,
            source is not null && SourceHasAllSelectedFields(source),
            false,
            null,
            join);
    }
}

public sealed record CteOutputFieldRowEditor(
    string Key,
    string DisplayText,
    QueryRowStateEditor State);

public sealed record CtePathRowEditor(
    string Key,
    string ObjectName,
    string DisplayText,
    QueryRowStateEditor State,
    bool HasAllSelectedFields,
    bool IsRoot,
    SourceRef? Source,
    JoinRef? Join)
{
    public string ActionText => HasAllSelectedFields ? "Clear all" : "Select all";

    public string ActionIcon => HasAllSelectedFields ? "remove_done" : "done_all";
}

public sealed record QueryRowStateEditor(bool IsSelected, bool IsFullySelected, bool IsOnActivePath)
{
    public static QueryRowStateEditor Inactive { get; } = new(false, false, false);

    public bool IsActive => IsSelected || IsOnActivePath;

    public TrimmedSqlLineState DisplayState => IsFullySelected
        ? TrimmedSqlLineState.Selected
        : IsSelected || IsOnActivePath
            ? TrimmedSqlLineState.Required
            : TrimmedSqlLineState.Removed;
}

public sealed record CteSelectionEditorStateSnapshot(
    IReadOnlyList<string> UserSelectedFields,
    IReadOnlyList<string> RequiredDependencyFields,
    IReadOnlyList<string> ActiveObjects,
    IReadOnlyList<string> FullySelectedObjects,
    IReadOnlyList<string> RootAnchorFields,
    IReadOnlyList<string> EmittedObjects);

