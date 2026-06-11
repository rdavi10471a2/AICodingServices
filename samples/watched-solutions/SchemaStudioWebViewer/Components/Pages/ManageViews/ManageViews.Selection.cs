using Radzen;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudioWebViewer.Components.Pages.ManageViews;

[AIChange("3.12", "2026-04-30 12:42 PM CDT updated Manage Views navigation prompts and guard checks so dirty saved-column edits block context changes like view-definition edits.", AICommandStatus.Pending)]
[AIChange("3.10", "2026-04-27 11:58 AM CDT moved Manage Views selection, workspace reload, reset, navigation guard, and selection-key helpers into a coarse feature partial.", AICommandStatus.Pending)]
public partial class ManageViews
{
    // 2026-04-30 12:42 PM CDT AI v3.12 marker: dirty saved-column edits now reuse the same context-switch guard as view-definition edits.
    private async Task OnDatabaseChanged(object value)
    {
        var previousDatabaseId = SelectedDatabase?.DatabaseId;
        if (await IsNavigationBlockedAsync(
            "Discard unsaved view or column changes and switch databases?",
            async () =>
            {
                SelectedDatabaseId = previousDatabaseId;
                await InvokeAsync(StateHasChanged);
            }))
        {
            return;
        }

        if (!int.TryParse(value?.ToString(), out var databaseId))
        {
            SelectedDatabaseId = null;
            SelectedDatabase = null;
            SelectedDomainFilter = null;
            LastAppliedDomainFilter = null;
            ResetWorkspace(WorkspaceResetLevel.Workspace);
            return;
        }

        SelectedDatabaseId = databaseId;
        ResetWorkspace(WorkspaceResetLevel.Workspace);
        await LoadWorkspaceAsync(databaseId, null, autoSelectFirst: false);
    }

    private Task OnDomainFilterChanged(object value)
    {
        return OnDomainFilterChangedCoreAsync(value?.ToString());
    }

    private async Task OnDomainFilterChangedCoreAsync(string? nextFilter)
    {
        if (await IsNavigationBlockedAsync(
            "Discard unsaved view or column changes and change the domain filter?",
            async () =>
            {
                SelectedDomainFilter = LastAppliedDomainFilter;
                await InvokeAsync(StateHasChanged);
            }))
        {
            return;
        }

        SelectedDomainFilter = nextFilter;
        LastAppliedDomainFilter = nextFilter;
        ResetWorkspace(WorkspaceResetLevel.View);

        await InvokeAsync(StateHasChanged);
    }

    private async Task OnViewSelectionChanged(object value)
    {
        var previousSelectionKey = SelectedViewItem?.SelectionKey;
        if (await IsNavigationBlockedAsync(
            "Discard unsaved view or column changes and open another view?",
            async () =>
            {
                SelectedViewKey = previousSelectionKey;
                await InvokeAsync(StateHasChanged);
            }))
        {
            return;
        }

        var key = value?.ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            ResetWorkspace(WorkspaceResetLevel.View);
            return;
        }

        await SelectViewAsync(key);
    }

    private async Task ReloadCurrentDatabaseAsync()
    {
        if (!SelectedDatabaseId.HasValue)
        {
            return;
        }

        ParserService.ClearCache();
        await LoadWorkspaceAsync(SelectedDatabaseId.Value, SelectedViewKey);
    }

    private async Task ResetEditorAsync()
    {
        if (!string.IsNullOrWhiteSpace(SelectedViewKey))
        {
            HeaderValidationMessage = string.Empty;
            await SelectViewAsync(SelectedViewKey);
        }
    }

    private async Task LoadWorkspaceAsync(int databaseId, string? preferredSelectionKey, bool autoSelectFirst = true)
    {
        IsBusy = true;

        try
        {
            SelectedDatabase = Databases.FirstOrDefault(x => x.DatabaseId == databaseId);
            Domains = (await DatabaseDomainRepository.GetByDatabaseIdAsync(databaseId)).ToList();

            var existingObjects = (await SchemaObjectRepository.GetByDatabaseAsync(databaseId)).ToList();
            var sourceCatalogDatabase = ResolveSourceCatalogDatabase(existingObjects);

            var availableSources = string.IsNullOrWhiteSpace(sourceCatalogDatabase)
                ? new List<SourceViewDefinition>()
                : (await SourceViewRepository.GetByDatabaseAsync(
                    sourceCatalogDatabase,
                    SelectedDatabase?.ViewNameFilter)).ToList();

            ExistingViewItems = existingObjects
                .OrderBy(x => string.IsNullOrWhiteSpace(x.BusinessName) ? x.SourceObjectName : x.BusinessName)
                .Select(x => new ViewWorkspaceItem
                {
                    SelectionKey = BuildExistingSelectionKey(x.SchemaObjectId),
                    IsExisting = true,
                    SchemaObjectId = x.SchemaObjectId,
                    SourceDatabaseName = x.SourceDatabaseName ?? SelectedDatabase?.DatabaseName ?? string.Empty,
                    SourceSchemaName = x.SourceSchemaName,
                    SourceObjectName = x.SourceObjectName,
                    Domain = x.Domain,
                    DisplayName = string.IsNullOrWhiteSpace(x.BusinessName) ? x.SourceObjectName : x.BusinessName!,
                    Subtitle = $"{x.SourceSchemaName}.{x.SourceObjectName}"
                })
                .ToList();

            var existingKeys = existingObjects
                .Select(x => $"{x.SourceSchemaName}.{x.SourceObjectName}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            AvailableViewItems = availableSources
                .Where(x => !existingKeys.Contains($"{x.SchemaName}.{x.ObjectName}"))
                .OrderBy(x => x.SchemaName)
                .ThenBy(x => x.ObjectName)
                .Select(x => new ViewWorkspaceItem
                {
                    SelectionKey = BuildAvailableSelectionKey(x.SchemaName, x.ObjectName),
                    IsExisting = false,
                    SourceDatabaseName = x.DatabaseName,
                    SourceSchemaName = x.SchemaName,
                    SourceObjectName = x.ObjectName,
                    DisplayName = GetAvailableDisplayName(x.ObjectName),
                    Subtitle = x.FullName
                })
                .ToList();

            LoadError = string.Empty;

            var nextSelection = preferredSelectionKey;
            if (autoSelectFirst &&
                (string.IsNullOrWhiteSpace(nextSelection) ||
                 !ExistingViewItems.Concat(AvailableViewItems).Any(x => x.SelectionKey == nextSelection)))
            {
                nextSelection = GetFirstAvailableSelectionKey();
            }

            SelectedViewKey = nextSelection;

            if (!string.IsNullOrWhiteSpace(nextSelection))
            {
                await SelectViewAsync(nextSelection);
            }
            else
            {
                ResetWorkspace(WorkspaceResetLevel.View);
            }
        }
        catch (Exception ex)
        {
            NotifyFailure("Workspace reload failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SelectViewAsync(string selectionKey)
    {
        IsBusy = true;

        try
        {
            SelectedViewKey = selectionKey;
            SelectedViewItem = FindWorkspaceItem(selectionKey);
            SelectedViewTabIndex = SelectedViewItem?.IsExisting == false ? 1 : 0;

            if (SelectedViewItem == null)
            {
                ResetWorkspace(WorkspaceResetLevel.View);
                return;
            }

            if (SelectedViewItem.IsExisting && SelectedViewItem.SchemaObjectId.HasValue)
            {
                var existing = await SchemaObjectRepository.GetByIdAsync(SelectedViewItem.SchemaObjectId.Value);
                EditableObject = existing == null ? null : CloneObject(existing);

                var existingColumns = existing == null
                    ? new List<SchemaObjectColumnDefinition>()
                    : (await SchemaObjectColumnRepository.GetByObjectAsync(existing.SchemaObjectId)).ToList();
                SavedColumns = existingColumns
                    .OrderBy(x => x.OrdinalPosition)
                    .ThenBy(x => x.SourceColumnName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                await ParseAndBuildReviewAsync(existingColumns);
            }
            else
            {
                EditableObject = CreateNewObjectDraft(SelectedViewItem);
                SavedColumns.Clear();
                await ParseAndBuildReviewAsync(Array.Empty<SchemaObjectColumnDefinition>());
            }

            if (EditableObject != null &&
                string.IsNullOrWhiteSpace(EditableObject.Domain))
            {
                EditableObject.Domain = GetPreferredDomain();
            }

            LoadError = string.Empty;
            HeaderValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            NotifyFailure("View selection failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> IsNavigationBlockedAsync(string prompt, Func<Task> restoreSelectionAsync)
    {
        if (await ConfirmDiscardChangesAsync(prompt))
        {
            return false;
        }

        await restoreSelectionAsync();
        return true;
    }

    private void ResetWorkspace(WorkspaceResetLevel level)
    {
        // 2026-04-27 11:58 AM CDT AI v3.10 marker: workspace reset and selection helpers live in this feature partial.
        SelectedViewKey = null;
        SelectedViewItem = null;
        EditableObject = null;
        CurrentParsedView = null;
        SavedColumns.Clear();
        ReviewRows.Clear();
        HeaderValidationMessage = string.Empty;
        SelectedEditorTabIndex = 0;

        if (level == WorkspaceResetLevel.Workspace)
        {
            SelectedDatabase = null;
            SelectedDomainFilter = null;
            LastAppliedDomainFilter = null;
            SelectedViewTabIndex = 0;
            Domains.Clear();
            ExistingViewItems.Clear();
            AvailableViewItems.Clear();
        }
    }

    private ViewWorkspaceItem? FindWorkspaceItem(string selectionKey)
    {
        return ExistingViewItems.Concat(AvailableViewItems)
            .FirstOrDefault(x => string.Equals(x.SelectionKey, selectionKey, StringComparison.Ordinal));
    }

    private string? GetFirstAvailableSelectionKey()
    {
        return ExistingViewItems.FirstOrDefault()?.SelectionKey
            ?? AvailableViewItems.FirstOrDefault()?.SelectionKey;
    }

    private static string BuildExistingSelectionKey(int schemaObjectId)
    {
        return $"existing:{schemaObjectId}";
    }

    private static string BuildAvailableSelectionKey(string schemaName, string objectName)
    {
        return $"available:{schemaName}.{objectName}";
    }

    private string GetAvailableDisplayName(string? objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return string.Empty;
        }

        var prefix = SelectedDatabase?.ViewNameFilter;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return objectName;
        }

        return objectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? objectName[prefix.Length..].TrimStart('_')
            : objectName;
    }

    private async Task<bool> ConfirmDiscardChangesAsync(string prompt)
    {
        if (!IsWorkspaceDirty)
        {
            return true;
        }

        var confirmed = await DialogService.Confirm(
            prompt,
            "Discard Changes",
            new ConfirmOptions { OkButtonText = "OK", CancelButtonText = "Cancel" });

        if (confirmed == true)
        {
            HeaderValidationMessage = string.Empty;
        }

        return confirmed == true;
    }
}
