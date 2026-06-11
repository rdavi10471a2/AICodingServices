using Radzen;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViews;

[AIChange("3.9", "2026-04-27 11:46 AM CDT moved Manage Views column merge and review-row helper logic into a coarse feature partial so the Razor page shell is easier to navigate.", AICommandStatus.Pending)]
public partial class ManageViews
{
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
            SelectedEditorTabIndex = 1;
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
                IsBaseDefinition = false,
                DisableInheritance = column.DisableInheritance,
                BusinessName = NormalizeNullableText(column.BusinessName),
                BusinessDescription = NormalizeNullableText(column.BusinessDescription),
                DeveloperNotes = null
            })
            .ToList();
    }

    private static IEnumerable<ManageViewsColumnReviewRow> BuildColumnReviewRows(
        IReadOnlyList<ViewColumnDto> parsedColumns,
        IEnumerable<SchemaObjectColumnDefinition> existingColumns)
    {
        // 2026-04-27 11:46 AM CDT AI v3.9 marker: column review mapping lives in this feature partial instead of the Razor page shell.
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

            var status = GetStatus(parsed, existing);
            yield return new ManageViewsColumnReviewRow
            {
                ColumnName = name,
                Status = status,
                ChangeSummary = BuildChangeSummary(parsed, existing, status),
                ParsedPreview = BuildParsedPreview(parsed),
                ExistingPreview = BuildExistingPreview(existing),
                AcceptMerge = status is "Added" or "Changed"
            };
        }
    }

    private static string GetStatus(ViewColumnDto? parsed, SchemaObjectColumnDefinition? existing)
    {
        if (parsed != null && existing == null)
        {
            return "Added";
        }

        if (parsed == null && existing != null)
        {
            return "Removed";
        }

        if (parsed == null || existing == null)
        {
            return "Unchanged";
        }

        return string.Equals(parsed.BusinessName, existing.BusinessName, StringComparison.Ordinal) &&
               string.Equals(parsed.BusinessDescription, existing.BusinessDescription, StringComparison.Ordinal)
            ? "Unchanged"
            : "Changed";
    }

    private static string BuildChangeSummary(ViewColumnDto? parsed, SchemaObjectColumnDefinition? existing, string status)
    {
        return status switch
        {
            "Added" => "Parser found a new column that is not yet in Schema Studio.",
            "Removed" => "Existing column is not present in the current parser result.",
            "Changed" => BuildChangedFieldSummary(parsed, existing),
            _ => "Business metadata matches the current saved record."
        };
    }

    private static string BuildChangedFieldSummary(ViewColumnDto? parsed, SchemaObjectColumnDefinition? existing)
    {
        var changes = new List<string>();

        if (!string.Equals(parsed?.BusinessName, existing?.BusinessName, StringComparison.Ordinal))
        {
            changes.Add("Business Name");
        }

        if (!string.Equals(parsed?.BusinessDescription, existing?.BusinessDescription, StringComparison.Ordinal))
        {
            changes.Add("Description");
        }

        return changes.Count == 0
            ? "Parser and existing values differ."
            : $"{string.Join(", ", changes)} differ.";
    }

    private static string BuildParsedPreview(ViewColumnDto? parsed)
    {
        if (parsed == null)
        {
            return "Not present in parser output.";
        }

        return $"Business Name: {parsed.BusinessName ?? "(blank)"}{Environment.NewLine}" +
               $"Description: {parsed.BusinessDescription ?? "(blank)"}";
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

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
