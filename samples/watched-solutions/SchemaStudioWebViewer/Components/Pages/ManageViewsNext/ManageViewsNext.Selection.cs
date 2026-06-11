using Radzen;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudioWebViewer.Components.Pages.ManageViewsNext;

[AIChange(
    "1.1",
    "2026-04-30 11:52 PM CDT blocked external selector and reload interactions while Manage Views Next is busy so async view loads cannot overlap or apply out of order.",
    AICommandStatus.Pending)]
[AIChange("1.0", "2026-04-30 03:37 PM CDT added database reload, domain tree loading, selection, dirty navigation guard, and reset handling for the Manage Views Next prototype.", AICommandStatus.Pending)]
public partial class ManageViewsNext
{
    // 2026-04-30 03:37 PM CDT AI v1.0 manage-views-next marker: selector flow reloads databases plus the selected workspace while keeping the prototype isolated from ManageViews.
    private async Task LoadDatabasesAsync(string? preferredSelectionKey)
    {
        await EnterBusyAsync();

        try
        {
            var previousDatabaseId = SelectedDatabaseId;
            Databases = (await DatabaseRepository.GetAllAsync()).ToList();

            if (Databases.Count == 0)
            {
                ResetWorkspace(WorkspaceResetLevel.Workspace);
                return;
            }

            SelectedDatabaseId = previousDatabaseId.HasValue && Databases.Any(x => x.DatabaseId == previousDatabaseId.Value)
                ? previousDatabaseId.Value
                : Databases[0].DatabaseId;

            await LoadWorkspaceAsync(
                SelectedDatabaseId.Value,
                preferredSelectionKey,
                autoSelectFirst: !string.IsNullOrWhiteSpace(preferredSelectionKey));
            LoadError = string.Empty;
        }
        catch (Exception ex)
        {
            NotifyFailure("Manage Views Next load failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadDatabasesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (await IsNavigationBlockedAsync(
            "Discard unsaved view or column changes and reload database metadata?",
            async () => await InvokeAsync(StateHasChanged)))
        {
            return;
        }

        ParserService.ClearCache();
        await LoadDatabasesAsync(SelectedViewKey);
    }

    private async Task OnDatabaseChanged(object value)
    {
        if (IsBusy)
        {
            return;
        }

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
        if (IsBusy)
        {
            return;
        }

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

    private async Task OnViewSelectionChanged(string? key)
    {
        if (IsBusy)
        {
            return;
        }

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

        if (string.IsNullOrWhiteSpace(key))
        {
            ResetWorkspace(WorkspaceResetLevel.View);
            return;
        }

        await SelectViewAsync(key);
    }

    private async Task ResetEditorAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedViewKey))
        {
            await SelectViewAsync(SelectedViewKey);
        }
    }

    private async Task LoadWorkspaceAsync(int databaseId, string? preferredSelectionKey, bool autoSelectFirst = true)
    {
        await EnterBusyAsync();

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
                .OrderBy(x => NormalizeDomain(x.Domain), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.IsBaseObject ? 0 : 1)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.BusinessName) ? x.SourceObjectName : x.BusinessName)
                .Select(x => new ViewWorkspaceItem
                {
                    SelectionKey = BuildExistingSelectionKey(x.SchemaObjectId),
                    IsExisting = true,
                    SchemaObjectId = x.SchemaObjectId,
                    IsBaseObject = x.IsBaseObject,
                    SourceDatabaseName = x.SourceDatabaseName ?? SelectedDatabase?.DatabaseName ?? string.Empty,
                    SourceSchemaName = x.SourceSchemaName,
                    SourceTableName = x.SourceTableName,
                    SourceObjectName = x.SourceObjectName,
                    Domain = NormalizeDomain(x.Domain),
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
                    Domain = UnknownDomain,
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
        await EnterBusyAsync();

        try
        {
            SelectedViewKey = selectionKey;
            SelectedViewItem = FindWorkspaceItem(selectionKey);

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

                SavedColumns = CloneColumns(existingColumns);
                OriginalSavedColumns = CloneColumns(existingColumns);
                await ParseAndBuildReviewAsync(existingColumns);
            }
            else
            {
                EditableObject = CreateNewObjectDraft(SelectedViewItem);
                SavedColumns.Clear();
                OriginalSavedColumns.Clear();
                await ParseAndBuildReviewAsync(Array.Empty<SchemaObjectColumnDefinition>());
            }

            if (EditableObject != null &&
                string.IsNullOrWhiteSpace(EditableObject.Domain))
            {
                EditableObject.Domain = GetPreferredDomain();
            }

            LoadError = string.Empty;
            EnsureSelectedColumn();
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
        SelectedViewKey = null;
        SelectedViewItem = null;
        EditableObject = null;
        CurrentParsedView = null;
        SavedColumns.Clear();
        OriginalSavedColumns.Clear();
        ReviewRows.Clear();
        SelectedColumnId = null;

        if (level == WorkspaceResetLevel.Workspace)
        {
            SelectedDatabase = null;
            SelectedDomainFilter = null;
            LastAppliedDomainFilter = null;
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

        return confirmed == true;
    }
}
