using Radzen;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.Dialogs;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViews;

[AIChange("3.15", "2026-04-30 11:27 AM CDT renamed the resolved dependency chain dialog title to View Details to match its expanded inspection role.", AICommandStatus.Pending)]
[AIChange("3.14", "2026-04-30 11:10 AM CDT made the resolved dependency chain dialog non-draggable and disabled overlay-click close while keeping resize enabled.", AICommandStatus.Pending)]
[AIChange("3.13", "2026-04-30 10:37 AM CDT constrained the resolved dependency chain dialog to viewport-relative sizing to avoid modal-level scrollbars.", AICommandStatus.Pending)]
[AIChange("3.12", "2026-04-30 10:24 AM CDT routed Manage Views dependency inspection to the tree-and-SQL resolved dependency chain dialog.", AICommandStatus.Pending)]
[AIChange("3.11", "2026-04-29 6:27 PM CDT renamed Manage Views parsed dependency dialog title to resolved dependency chain.", AICommandStatus.Pending)]
[AIChange("3.10", "2026-04-27 11:58 AM CDT moved Manage Views parser refresh, SQL display, parsed dependency, and where-used actions into a coarse feature partial.", AICommandStatus.Pending)]
public partial class ManageViews
{
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
        ReviewRows = BuildColumnReviewRows(parsedColumns, existingColumns).ToList();
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
            // 2026-04-27 11:58 AM CDT AI v3.10 marker: parser refresh and dependency tools live in this feature partial.
            CurrentParsedView = ParserService.ReloadView(
                EditableObject.SourceDatabaseName ?? string.Empty,
                EditableObject.SourceSchemaName,
                EditableObject.SourceObjectName);

            var parsedColumns = CurrentParsedView?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
            ReviewRows = BuildColumnReviewRows(parsedColumns, SavedColumns).ToList();
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
            // 2026-04-30 11:27 AM CDT AI v3.15 marker: this dialog now presents full view details, not just dependency edges.
            $"View Details: {EditableObject.SourceObjectName}",
            new Dictionary<string, object?>
            {
                { "ParsedView", CurrentParsedView },
                { "SqlContent", CurrentParsedView.SourceQuery ?? string.Empty }
            },
            new DialogOptions
            {
                // 2026-04-30 11:10 AM CDT AI v3.14 marker: keep this large dependency surface modal and resizable, but prevent off-screen dragging and accidental overlay close.
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
}
