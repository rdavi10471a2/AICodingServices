using Radzen;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.Pages.ManageViews;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViewsNext;

[AIChange("1.0", "2026-04-30 03:37 PM CDT added column selector, selected-column reset, merge review, and parser-to-column mapping helpers for the Manage Views Next prototype.", AICommandStatus.Pending)]
public partial class ManageViewsNext
{
    // 2026-04-30 03:37 PM CDT AI v1.0 manage-views-next marker: column editor behavior stays in a dedicated partial so the prototype page shell remains readable.
    private int? SelectedColumnId;
    private string ColumnFilter = string.Empty;

    private List<SchemaObjectColumnDefinition> FilteredColumns
    {
        get
        {
            var query = SavedColumns
                .OrderBy(column => column.MergeState == SchemaObjectColumnMergeState.DetectedRemove ? int.MaxValue - 1 : column.OrdinalPosition)
                .ThenBy(column => column.SourceColumnName, StringComparer.OrdinalIgnoreCase)
                .AsEnumerable();
            if (!string.IsNullOrWhiteSpace(ColumnFilter))
            {
                query = query.Where(ColumnMatchesFilter);
            }

            return query.ToList();
        }
    }

    private SchemaObjectColumnDefinition? SelectedColumn =>
        SelectedColumnId == null
            ? null
            : SavedColumns.FirstOrDefault(column => GetColumnKey(column) == SelectedColumnId.Value);

    private bool SelectedColumnMetadataIsInherited =>
        SelectedColumn != null &&
        EditableObject?.IsBaseObject != true &&
        !SelectedColumn.DisableInheritance;

    private string? SelectedColumnInheritedFrom =>
        SelectedColumnMetadataIsInherited
            ? FormatQualifiedName(
                SelectedColumn?.SemanticDatabase,
                SelectedColumn?.SemanticSchema,
                SelectedColumn?.SemanticObject,
                SelectedColumn?.SemanticColumn)
            : null;

    private void EnsureSelectedColumn()
    {
        if (SelectedColumn != null && FilteredColumns.Any(column => GetColumnKey(column) == SelectedColumnId))
        {
            return;
        }

        SelectedColumnId = FilteredColumns.FirstOrDefault() is { } first
            ? GetColumnKey(first)
            : null;
    }

    private void SelectColumn(SchemaObjectColumnDefinition column)
    {
        SelectedColumnId = GetColumnKey(column);
    }

    private async Task ResetSelectedColumnAsync()
    {
        if (SelectedColumn == null)
        {
            return;
        }

        var selectedKey = GetColumnKey(SelectedColumn);
        var original = OriginalSavedColumns.FirstOrDefault(column => GetColumnKey(column) == selectedKey);
        if (original == null)
        {
            return;
        }

        SelectedColumn.BusinessName = original.BusinessName;
        SelectedColumn.BusinessDescription = original.BusinessDescription;
        SelectedColumn.DeveloperNotes = original.DeveloperNotes;
        SelectedColumn.DisableInheritance = original.DisableInheritance;
        SelectedColumn.ClearDirty();
        await InvokeAsync(StateHasChanged);
    }

    private string GetColumnSelectorClass(SchemaObjectColumnDefinition column) =>
        SelectedColumn != null && GetColumnKey(column) == GetColumnKey(SelectedColumn)
            ? "mvn-column-item selected"
            : "mvn-column-item";

    private static string GetColumnStateChipText(SchemaObjectColumnDefinition column) =>
        column.MergeState switch
        {
            SchemaObjectColumnMergeState.DetectedAdd or SchemaObjectColumnMergeState.PendingAdd => "Added",
            SchemaObjectColumnMergeState.DetectedRemove or SchemaObjectColumnMergeState.PendingRemove => "Removed",
            SchemaObjectColumnMergeState.PendingUpdate => "Edited",
            _ => "Edited"
        };

    private static string GetColumnStateChipClass(SchemaObjectColumnDefinition column) =>
        column.MergeState switch
        {
            SchemaObjectColumnMergeState.DetectedAdd or SchemaObjectColumnMergeState.PendingAdd => "mvn-state-chip added",
            SchemaObjectColumnMergeState.DetectedRemove or SchemaObjectColumnMergeState.PendingRemove => "mvn-state-chip removed",
            SchemaObjectColumnMergeState.PendingUpdate => "mvn-state-chip edited",
            _ => "mvn-state-chip edited"
        };

    private bool ColumnMatchesFilter(SchemaObjectColumnDefinition column)
    {
        var filter = ColumnFilter.Trim();
        return Contains(column.SourceColumnName, filter)
            || Contains(column.BusinessName, filter)
            || Contains(column.BusinessDescription, filter)
            || Contains(column.DeveloperNotes, filter);
    }

    private static bool Contains(string? value, string filter) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static int GetColumnKey(SchemaObjectColumnDefinition column) =>
        column.SchemaObjectColumnId > 0
            ? column.SchemaObjectColumnId
            : HashCode.Combine(column.SchemaObjectId, column.OrdinalPosition, column.SourceColumnName?.ToUpperInvariant());

    private static string BuildColumnTooltip(SchemaObjectColumnDefinition column)
    {
        if (!string.IsNullOrWhiteSpace(column.BusinessDescription))
        {
            return column.BusinessDescription;
        }

        if (!string.IsNullOrWhiteSpace(column.BusinessName))
        {
            return column.BusinessName;
        }

        return column.SourceColumnName;
    }

    private async Task ReviewMergeAsync()
    {
        if (EditableObject == null || SelectedViewItem == null || !SelectedDatabaseId.HasValue)
        {
            return;
        }

        if (EditableObject.SchemaObjectId <= 0)
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Save view first", "Save the view definition before initializing child columns from the parser review.", 3500);
            return;
        }

        var addedRows = ReviewRows
            .Where(x => string.Equals(x.Status, "Added", StringComparison.Ordinal))
            .ToList();

        if (addedRows.Count == 0)
        {
            NotificationService.Notify(NotificationSeverity.Info, "No added columns", "The current parser result does not contain any added columns to initialize.", 3000);
            return;
        }

        var result = await DialogService.OpenAsync<ManageViewsReviewMergeDialog>(
            $"{SelectedViewItem.DisplayName} - Review Merge",
            new Dictionary<string, object?>
            {
                { "ViewDisplayName", SelectedViewItem.DisplayName },
                { "Rows", addedRows }
            },
            new DialogOptions
            {
                Width = "1100px",
                Height = "720px",
                Resizable = true,
                Draggable = true
            });

        var acceptedColumnNames = (result as IEnumerable<string>)?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (acceptedColumnNames == null || acceptedColumnNames.Count == 0)
        {
            return;
        }

        var columnsToSave = BuildAcceptedAddedColumns(acceptedColumnNames);
        if (columnsToSave.Count == 0)
        {
            NotificationService.Notify(NotificationSeverity.Info, "Nothing to save", "No added parser columns remained to save after review.", 3000);
            return;
        }

        IsBusy = true;

        try
        {
            foreach (var column in columnsToSave)
            {
                column.SchemaObjectId = EditableObject.SchemaObjectId;
            }

            await SchemaObjectColumnRepository.SaveAllAsync(columnsToSave);
            NotificationService.Notify(NotificationSeverity.Success, "Columns initialized", $"{columnsToSave.Count} added columns were saved to the selected view.", 3000);
            await LoadWorkspaceAsync(SelectedDatabaseId.Value, BuildExistingSelectionKey(EditableObject.SchemaObjectId), autoSelectFirst: false);
        }
        catch (Exception ex)
        {
            NotifyFailure("Merge save failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<SchemaObjectColumnDefinition> BuildAcceptedAddedColumns(IReadOnlyCollection<string> acceptedColumnNames)
    {
        if (EditableObject == null || CurrentParsedView == null || acceptedColumnNames.Count == 0)
        {
            return new List<SchemaObjectColumnDefinition>();
        }

        var acceptedSet = acceptedColumnNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingNames = SavedColumns
            .Where(column => !string.IsNullOrWhiteSpace(column.SourceColumnName))
            .Select(column => column.SourceColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return CurrentParsedView.Columns
            .Where(column => !string.IsNullOrWhiteSpace(column.ColumnName))
            .Where(column => acceptedSet.Contains(column.ColumnName!))
            .Where(column => !existingNames.Contains(column.ColumnName!))
            .OrderBy(column => column.OrdinalPosition)
            .Select(column => new SchemaObjectColumnDefinition
            {
                SchemaObjectColumnId = 0,
                SchemaObjectId = EditableObject.SchemaObjectId,
                OrdinalPosition = column.OrdinalPosition,
                SourceColumnName = column.ColumnName ?? string.Empty,
                SourceColumnKind = NormalizeNullableText(column.ColumnKind.ToString()),
                BaseDatabaseName = NormalizeNullableText(column.BaseDatabase),
                BaseSchemaName = NormalizeNullableText(column.BaseSchema),
                BaseObjectName = NormalizeNullableText(column.BaseTable),
                BaseColumnName = NormalizeNullableText(column.BaseColumn),
                SemanticDatabase = NormalizeNullableText(column.SemanticDatabase),
                SemanticSchema = NormalizeNullableText(column.SemanticSchema),
                SemanticObject = NormalizeNullableText(column.SemanticObject),
                SemanticColumn = NormalizeNullableText(column.SemanticColumn),
                IsBaseDefinition = EditableObject.IsBaseObject,
                DisableInheritance = column.DisableInheritance,
                BusinessName = NormalizeNullableText(column.BusinessName),
                BusinessDescription = NormalizeNullableText(column.BusinessDescription),
                DeveloperNotes = null
            })
            .ToList();
    }

    private static IEnumerable<ManageViewsColumnReviewRow> BuildColumnReviewRows(
        IReadOnlyList<ViewColumnDto> parsedColumns,
        IEnumerable<SchemaObjectColumnDefinition> existingColumns,
        bool isBaseView)
    {
        var parsedByName = parsedColumns
            .Where(x => !string.IsNullOrWhiteSpace(x.ColumnName))
            .GroupBy(x => x.ColumnName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var existingByName = existingColumns
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceColumnName))
            .GroupBy(x => x.SourceColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var allNames = parsedByName.Keys
            .Union(existingByName.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => parsedByName.TryGetValue(name, out var parsed) ? parsed.OrdinalPosition : int.MaxValue)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
        {
            parsedByName.TryGetValue(name, out var parsed);
            existingByName.TryGetValue(name, out var existing);

            var status = GetStatus(parsed, existing, isBaseView);
            yield return new ManageViewsColumnReviewRow
            {
                ColumnName = name,
                Status = status,
                ChangeSummary = BuildChangeSummary(parsed, existing, status, isBaseView),
                ParsedPreview = BuildParsedPreview(parsed),
                ExistingPreview = BuildExistingPreview(existing),
                AcceptMerge = status is "Added" or "Changed"
            };
        }
    }

    private static string GetStatus(ViewColumnDto? parsed, SchemaObjectColumnDefinition? existing, bool isBaseView)
    {
        if (parsed != null && existing == null)
        {
            return "Added";
        }

        if (existing?.MergeState is SchemaObjectColumnMergeState.DetectedAdd or SchemaObjectColumnMergeState.PendingAdd)
        {
            return "Added";
        }

        if (existing?.MergeState is SchemaObjectColumnMergeState.DetectedRemove or SchemaObjectColumnMergeState.PendingRemove)
        {
            return "Removed";
        }

        if (parsed == null && existing != null)
        {
            return "Removed";
        }

        if (parsed == null || existing == null)
        {
            return "Unchanged";
        }

        if (!ParserShapeMatches(parsed, existing))
        {
            return "Changed";
        }

        if (!isBaseView)
        {
            return "Unchanged";
        }

        return BusinessMetadataMatches(parsed, existing)
            ? "Unchanged"
            : "Changed";
    }

    private static string BuildChangeSummary(ViewColumnDto? parsed, SchemaObjectColumnDefinition? existing, string status, bool isBaseView)
    {
        return status switch
        {
            "Added" => "Parser found a new column that is not yet in Schema Studio.",
            "Removed" => "Existing column is not present in the current parser result.",
            "Changed" => BuildChangedFieldSummary(parsed, existing, isBaseView),
            _ => "Business metadata matches the current saved record."
        };
    }

    private static string BuildChangedFieldSummary(ViewColumnDto? parsed, SchemaObjectColumnDefinition? existing, bool isBaseView)
    {
        var changes = new List<string>();

        if (!ParserShapeMatches(parsed, existing))
        {
            changes.Add("parser-owned source mapping");
        }

        if (isBaseView && !string.Equals(NormalizeNullableText(parsed?.BusinessName), NormalizeNullableText(existing?.BusinessName), StringComparison.Ordinal))
        {
            changes.Add("Business Name");
        }

        if (isBaseView && !string.Equals(NormalizeNullableText(parsed?.BusinessDescription), NormalizeNullableText(existing?.BusinessDescription), StringComparison.Ordinal))
        {
            changes.Add("Description");
        }

        if (isBaseView && parsed != null && existing != null && parsed.DisableInheritance != existing.DisableInheritance)
        {
            changes.Add("Disable Inheritance");
        }

        return changes.Count == 0
            ? "Parser and existing values differ."
            : $"{string.Join(", ", changes)} differ.";
    }

    private static bool BusinessMetadataMatches(ViewColumnDto parsed, SchemaObjectColumnDefinition existing) =>
        string.Equals(NormalizeNullableText(parsed.BusinessName), NormalizeNullableText(existing.BusinessName), StringComparison.Ordinal) &&
        string.Equals(NormalizeNullableText(parsed.BusinessDescription), NormalizeNullableText(existing.BusinessDescription), StringComparison.Ordinal) &&
        parsed.DisableInheritance == existing.DisableInheritance;

    private static bool ParserShapeMatches(ViewColumnDto? parsed, SchemaObjectColumnDefinition? existing)
    {
        if (parsed == null || existing == null)
        {
            return true;
        }

        return parsed.OrdinalPosition == existing.OrdinalPosition
            && string.Equals(NormalizeNullableText(parsed.ColumnKind), NormalizeNullableText(existing.SourceColumnKind), StringComparison.Ordinal)
            && string.Equals(FormatQualifiedName(parsed.BaseDatabase, parsed.BaseSchema, parsed.BaseTable, parsed.BaseColumn), FormatQualifiedName(existing.BaseDatabaseName, existing.BaseSchemaName, existing.BaseObjectName, existing.BaseColumnName), StringComparison.Ordinal)
            && string.Equals(FormatQualifiedName(parsed.SemanticDatabase, parsed.SemanticSchema, parsed.SemanticObject, parsed.SemanticColumn), FormatQualifiedName(existing.SemanticDatabase, existing.SemanticSchema, existing.SemanticObject, existing.SemanticColumn), StringComparison.Ordinal);
    }

    private static string? FormatQualifiedName(string? database, string? schema, string? objectName, string? columnName)
    {
        var parts = new[] { database, schema, objectName, columnName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToList();

        return parts.Count == 0 ? null : string.Join(".", parts);
    }

    private static string BuildParsedPreview(ViewColumnDto? parsed)
    {
        if (parsed == null)
        {
            return "Not present in parser output.";
        }

        return $"Business Name: {parsed.BusinessName ?? "(blank)"}{Environment.NewLine}" +
               $"Description: {parsed.BusinessDescription ?? "(blank)"}{Environment.NewLine}" +
               $"Disable Inheritance: {parsed.DisableInheritance}";
    }

    private static string BuildExistingPreview(SchemaObjectColumnDefinition? existing)
    {
        if (existing == null)
        {
            return "Not yet saved in Schema Studio.";
        }

        return $"Business Name: {existing.BusinessName ?? "(blank)"}{Environment.NewLine}" +
               $"Description: {existing.BusinessDescription ?? "(blank)"}{Environment.NewLine}" +
               $"Disable Inheritance: {existing.DisableInheritance}";
    }

    private static List<SchemaObjectColumnDefinition> CloneColumns(IEnumerable<SchemaObjectColumnDefinition> columns)
    {
        return columns
            .OrderBy(x => x.OrdinalPosition)
            .ThenBy(x => x.SourceColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(CloneColumn)
            .ToList();
    }

    private static SchemaObjectColumnDefinition CloneColumn(SchemaObjectColumnDefinition source)
    {
        var clone = new SchemaObjectColumnDefinition
        {
            SchemaObjectColumnId = source.SchemaObjectColumnId,
            SchemaObjectId = source.SchemaObjectId,
            OrdinalPosition = source.OrdinalPosition,
            SourceColumnName = source.SourceColumnName,
            SourceColumnKind = source.SourceColumnKind,
            BaseDatabaseName = source.BaseDatabaseName,
            BaseSchemaName = source.BaseSchemaName,
            BaseObjectName = source.BaseObjectName,
            BaseColumnName = source.BaseColumnName,
            SemanticDatabase = source.SemanticDatabase,
            SemanticSchema = source.SemanticSchema,
            SemanticObject = source.SemanticObject,
            SemanticColumn = source.SemanticColumn,
            IsBaseDefinition = source.IsBaseDefinition,
            DisableInheritance = source.DisableInheritance,
            BusinessName = source.BusinessName,
            BusinessDescription = source.BusinessDescription,
            DeveloperNotes = source.DeveloperNotes,
            LastSynced = source.LastSynced,
            MergeState = source.MergeState
        };

        clone.ClearDirty();
        return clone;
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
