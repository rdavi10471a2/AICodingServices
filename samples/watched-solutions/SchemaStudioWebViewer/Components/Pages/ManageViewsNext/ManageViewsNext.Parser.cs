using Radzen;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.Dialogs;
using SchemaStudioWebViewer.Components.Pages.ManageViews;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViewsNext;

[AIChange("1.0", "2026-04-30 03:37 PM CDT added parser refresh, SQL, view-details, and where-used actions for the Manage Views Next prototype.", AICommandStatus.Pending)]
public partial class ManageViewsNext
{
    // 2026-04-30 03:37 PM CDT AI v1.0 manage-views-next marker: parser actions mirror ManageViews while keeping the prototype page separately testable.
    private async Task ParseAndBuildReviewAsync(IEnumerable<SchemaObjectColumnDefinition> existingColumns)
    {
        if (EditableObject == null)
        {
            CurrentParsedView = null;
            ReviewRows.Clear();
            return;
        }

        CurrentParsedView = ParserService.ParseView(
            EditableObject.SourceDatabaseName ?? string.Empty,
            EditableObject.SourceSchemaName,
            EditableObject.SourceObjectName);

        var parsedColumns = CurrentParsedView?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
        ReviewRows = BuildColumnReviewRows(parsedColumns, existingColumns, EditableObject.IsBaseObject).ToList();
        ApplyDetectedColumnStates(parsedColumns);
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshCurrentViewAsync()
    {
        if (EditableObject == null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            CurrentParsedView = ParserService.ReloadView(
                EditableObject.SourceDatabaseName ?? string.Empty,
                EditableObject.SourceSchemaName,
                EditableObject.SourceObjectName);

            var parsedColumns = CurrentParsedView?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
            ReviewRows = BuildColumnReviewRows(parsedColumns, SavedColumns, EditableObject.IsBaseObject).ToList();
            ApplyDetectedColumnStates(parsedColumns);
            NotificationService.Notify(NotificationSeverity.Success, "View refreshed", "The selected view SQL and parser review state were refreshed.", 2500);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            NotifyFailure("View refresh failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShowSqlAsync()
    {
        if (EditableObject == null)
        {
            return;
        }

        try
        {
            ParserService.ClearCache();
            var sqlContent = ParserService.GetViewSql(
                EditableObject.SourceDatabaseName ?? string.Empty,
                EditableObject.SourceSchemaName,
                EditableObject.SourceObjectName) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sqlContent))
            {
                NotificationService.Notify(NotificationSeverity.Warning, "Parser SQL unavailable", "The parser service did not return SQL for the selected view.");
                return;
            }

            await DialogService.OpenAsync<ViewSQLDialog>(
                $"Parser SQL: {EditableObject.SourceObjectName}",
                new Dictionary<string, object?>
                {
                    { "SqlContent", sqlContent },
                    { "IsMock", false }
                },
                new DialogOptions
                {
                    Width = "1200px",
                    Height = "820px",
                    Resizable = true,
                    Draggable = true
                });
        }
        catch (Exception ex)
        {
            NotifyFailure("Parser SQL failed", ex);
        }
    }

    private async Task ShowParsedDependenciesAsync()
    {
        if (CurrentParsedView == null || EditableObject == null)
        {
            return;
        }

        await DialogService.OpenAsync<ResolvedDependencyChainDialog>(
            $"Current Parsed Shape: {EditableObject.SourceObjectName}",
            new Dictionary<string, object?>
            {
                { "ParsedView", CurrentParsedView },
                { "SqlContent", CurrentParsedView.SourceQuery ?? string.Empty },
                { "SavedColumns", SavedColumns.ToList() },
                { "IsBaseObject", EditableObject.IsBaseObject }
            },
            new DialogOptions
            {
                Width = "min(1320px, calc(100vw - 96px))",
                Height = "calc(100vh - 120px)",
                Resizable = true,
                Draggable = false,
                CloseDialogOnOverlayClick = false
            });
    }

    private async Task ShowWhereUsedAsync()
    {
        if (EditableObject == null)
        {
            return;
        }

        try
        {
            var dependencies = await SqlObjectDependencyRepository.GetWhereUsedAsync(
                EditableObject.SourceDatabaseName ?? string.Empty,
                EditableObject.SourceSchemaName,
                EditableObject.SourceObjectName);

            if (dependencies.Count == 0)
            {
                NotificationService.Notify(NotificationSeverity.Info, "No where-used rows", "SQL Server dependency metadata does not list any objects using the selected object.", 3000);
                return;
            }

            await DialogService.OpenAsync<SqlObjectDependenciesDialog>(
                $"Where Used: {EditableObject.SourceObjectName}",
                new Dictionary<string, object?>
                {
                    { "Dependencies", dependencies }
                },
                new DialogOptions
                {
                    Width = "1100px",
                    Height = "620px",
                    Resizable = true,
                    Draggable = true
                });
        }
        catch (Exception ex)
        {
            NotifyFailure("Where-used lookup failed", ex);
        }
    }

    private void ApplyDetectedColumnStates(IReadOnlyList<ViewColumnDto> parsedColumns)
    {
        if (EditableObject == null)
        {
            return;
        }

        SavedColumns.RemoveAll(column => column.MergeState == SchemaObjectColumnMergeState.DetectedAdd);

        foreach (var column in SavedColumns.Where(column => column.MergeState == SchemaObjectColumnMergeState.DetectedRemove))
        {
            column.MergeState = SchemaObjectColumnMergeState.None;
        }

        var savedByName = SavedColumns
            .Where(column => !string.IsNullOrWhiteSpace(column.SourceColumnName))
            .GroupBy(column => column.SourceColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var parsedByName = parsedColumns
            .Where(column => !string.IsNullOrWhiteSpace(column.ColumnName))
            .GroupBy(column => column.ColumnName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var savedColumn in SavedColumns.Where(column => !string.IsNullOrWhiteSpace(column.SourceColumnName)))
        {
            if (!parsedByName.ContainsKey(savedColumn.SourceColumnName) &&
                savedColumn.MergeState == SchemaObjectColumnMergeState.None)
            {
                savedColumn.MergeState = SchemaObjectColumnMergeState.DetectedRemove;
            }
        }

        foreach (var parsedColumn in parsedByName.Values.OrderBy(column => column.OrdinalPosition))
        {
            if (savedByName.ContainsKey(parsedColumn.ColumnName!))
            {
                continue;
            }

            SavedColumns.Add(CreateDetectedColumn(parsedColumn));
        }
    }

    private SchemaObjectColumnDefinition CreateDetectedColumn(ViewColumnDto parsed)
    {
        var column = new SchemaObjectColumnDefinition
        {
            SchemaObjectColumnId = 0,
            SchemaObjectId = EditableObject?.SchemaObjectId ?? 0,
            OrdinalPosition = parsed.OrdinalPosition,
            SourceColumnName = parsed.ColumnName ?? string.Empty,
            SourceColumnKind = NormalizeNullableText(parsed.ColumnKind),
            BaseDatabaseName = NormalizeNullableText(parsed.BaseDatabase),
            BaseSchemaName = NormalizeNullableText(parsed.BaseSchema),
            BaseObjectName = NormalizeNullableText(parsed.BaseTable),
            BaseColumnName = NormalizeNullableText(parsed.BaseColumn),
            SemanticDatabase = NormalizeNullableText(parsed.SemanticDatabase),
            SemanticSchema = NormalizeNullableText(parsed.SemanticSchema),
            SemanticObject = NormalizeNullableText(parsed.SemanticObject),
            SemanticColumn = NormalizeNullableText(parsed.SemanticColumn),
            IsBaseDefinition = EditableObject?.IsBaseObject == true,
            DisableInheritance = GetParsedDisableInheritance(parsed),
            BusinessName = NormalizeNullableText(parsed.BusinessName),
            BusinessDescription = NormalizeNullableText(parsed.BusinessDescription),
            DeveloperNotes = null,
            MergeState = SchemaObjectColumnMergeState.DetectedAdd
        };

        column.ClearDirty();
        return column;
    }

    private bool GetParsedDisableInheritance(ViewColumnDto parsed)
    {
        if (CurrentParsedView == null || string.IsNullOrWhiteSpace(parsed.ColumnName))
        {
            return false;
        }

        return CurrentParsedView.Columns
            .FirstOrDefault(column => string.Equals(column.ColumnName, parsed.ColumnName, StringComparison.OrdinalIgnoreCase))
            ?.DisableInheritance == true;
    }
}
