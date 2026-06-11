using Microsoft.AspNetCore.Components;
using SchemaStudio.Data.Models;

[module: SchemaStudio.AIHelpers.AIFileContext(
    "Components/Pages/DomainObjectModeler/DomainObjectModeler.Selection.cs",
    "Selection and loading workflow for the Domain Object Modeler page.",
    Responsibilities = "Load databases, domains, and domain-filtered base views, then keep selection, anchor, alias, and join-row state coherent.",
    RelatedFiles = "Components/Pages/DomainObjectModeler/DomainObjectModeler.razor; SchemaStudio.Data/Repositories/SchemaObjectRepository.cs",
    LastReviewed = "2026-05-13")]

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private async Task LoadDatabasesAsync()
    {
        IsBusy = true;
        LoadError = string.Empty;

        try
        {
            Databases = (await DatabaseRepository.GetAllAsync())
                .Where(database => database.Active)
                .OrderBy(database => database.DatabaseName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StatusMessage = Databases.Count == 0
                ? "No active databases are configured."
                : "Select a database and domain to begin.";
        }
        catch (Exception ex)
        {
            NotifyFailure("Domain Object Modeler load failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnDatabaseChanged(object? value)
    {
        SelectedDatabaseId = value is int id ? id : null;
        SelectedDomain = string.Empty;
        Domains.Clear();
        BaseViews.Clear();
        JoinRows.Clear();
        SourceDatabaseRelationships.Clear();
        GeneratedSql = string.Empty;
        NextSelectionOrdinal = 1;

        if (!SelectedDatabaseId.HasValue)
        {
            StatusMessage = "Select a database and domain to begin.";
            return;
        }

        IsBusy = true;

        try
        {
            Domains = (await DatabaseDomainRepository.GetByDatabaseIdAsync(SelectedDatabaseId.Value))
                .OrderBy(domain => domain.Domain, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StatusMessage = Domains.Count == 0
                ? "No domains are configured for the selected database."
                : "Select a domain to load base views.";
        }
        catch (Exception ex)
        {
            NotifyFailure("Domain load failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnDomainChanged(object? value)
    {
        SelectedDomain = value?.ToString() ?? string.Empty;
        await LoadBaseViewsAsync();
    }

    private async Task RefreshCatalogAsync()
    {
        if (SelectedDatabaseId.HasValue && !string.IsNullOrWhiteSpace(SelectedDomain))
        {
            await LoadBaseViewsAsync();
            return;
        }

        await LoadDatabasesAsync();
    }

    private async Task LoadBaseViewsAsync()
    {
        BaseViews.Clear();
        JoinRows.Clear();
        SourceDatabaseRelationships.Clear();
        GeneratedSql = string.Empty;

        if (!SelectedDatabaseId.HasValue || string.IsNullOrWhiteSpace(SelectedDomain))
        {
            StatusMessage = "Select a database and domain to load base views.";
            return;
        }

        IsBusy = true;

        try
        {
            var rows = await SchemaObjectRepository.GetBaseObjectsByDatabaseAndDomainAsync(
                SelectedDatabaseId.Value,
                SelectedDomain);

            BaseViews = rows
                .Select(source =>
                {
                    var relationshipTableName = ResolveRelationshipTableName(source, SelectedDatabase?.ViewNameFilter);
                    return new DomainBaseViewItem
                    {
                        Source = source,
                        RelationshipTableName = relationshipTableName,
                        AliasName = BuildDefaultAlias(relationshipTableName)
                    };
                })
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await LoadSourceDatabaseRelationshipsAsync();

            if (string.IsNullOrWhiteSpace(TargetViewName))
            {
                TargetViewName = GetConfiguredViewNamePrefix();
            }

            StatusMessage = BaseViews.Count == 0
                ? $"No active base views are registered for {SelectedDomain}."
                : $"Loaded {BaseViews.Count} base views for {SelectedDomain}.";
        }
        catch (Exception ex)
        {
            NotifyFailure("Base view load failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ToggleBaseView(DomainBaseViewItem item, bool isSelected)
    {
        item.IsSelected = isSelected;
        GeneratedSql = string.Empty;

        if (!item.IsSelected)
        {
            item.IsAnchor = false;
            item.SelectionOrdinal = 0;
            JoinRows.RemoveAll(row => row.SchemaObjectId == item.SchemaObjectId);
        }
        else
        {
            if (item.SelectionOrdinal == 0)
            {
                item.SelectionOrdinal = NextSelectionOrdinal++;
            }

            if (AnchorView is null)
            {
                SetAnchor(item);
            }
        }

        EnsureJoinRows();
    }

    private void MoveSelectedElement(DomainBaseViewItem item, int direction)
    {
        var anchor = AnchorView;
        var ordered = NonAnchorSelectedBaseViews.ToList();
        var index = ordered.IndexOf(item);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= ordered.Count)
        {
            return;
        }

        (ordered[index], ordered[targetIndex]) = (ordered[targetIndex], ordered[index]);
        var nextOrdinal = 1;
        if (anchor is not null)
        {
            anchor.SelectionOrdinal = nextOrdinal++;
        }

        for (var ordinal = 0; ordinal < ordered.Count; ordinal++)
        {
            ordered[ordinal].SelectionOrdinal = nextOrdinal++;
        }

        NextSelectionOrdinal = nextOrdinal;
        GeneratedSql = string.Empty;
    }

    private int GetJoinElementIndex(DomainBaseViewItem item) =>
        NonAnchorSelectedBaseViews.ToList().IndexOf(item);

    private void SetAnchor(DomainBaseViewItem item)
    {
        if (!item.IsSelected)
        {
            item.IsSelected = true;
        }

        if (item.SelectionOrdinal == 0)
        {
            item.SelectionOrdinal = NextSelectionOrdinal++;
        }

        foreach (var baseView in BaseViews)
        {
            baseView.IsAnchor = ReferenceEquals(baseView, item);
        }

        GeneratedSql = string.Empty;
        StatusMessage = $"FROM anchor changed to {item.DisplayName}. Check join clauses for the remaining elements.";
        EnsureJoinRows();
    }

    private void OnAnchorChanged(object? value)
    {
        if (value is not int schemaObjectId)
        {
            return;
        }

        var item = FindBaseView(schemaObjectId);
        if (item is not null)
        {
            SetAnchor(item);
        }
    }

    private void EnsureJoinRows()
    {
        var requiredIds = NonAnchorSelectedBaseViews
            .Select(item => item.SchemaObjectId)
            .ToHashSet();

        JoinRows.RemoveAll(row => !requiredIds.Contains(row.SchemaObjectId));

        foreach (var item in NonAnchorSelectedBaseViews)
        {
            if (FindJoinRow(item.SchemaObjectId) is null)
            {
                JoinRows.Add(new DomainObjectJoinRow
                {
                    SchemaObjectId = item.SchemaObjectId
                });
            }
        }

        ApplyRelationshipDefaultsToJoinRows(overwriteInferred: false);
    }

    private DomainObjectJoinRow? FindJoinRow(int schemaObjectId) =>
        JoinRows.FirstOrDefault(row => row.SchemaObjectId == schemaObjectId);

    private DomainBaseViewItem? FindBaseView(int schemaObjectId) =>
        BaseViews.FirstOrDefault(item => item.SchemaObjectId == schemaObjectId);

    private DomainBaseViewItem? FindBaseViewBySourceName(string sourceName) =>
        BaseViews.FirstOrDefault(item =>
            string.Equals(item.RelationshipTableName, sourceName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Source.SourceObjectName, sourceName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SanitizeIdentifierToken(item.Source.SourceObjectName), sourceName, StringComparison.OrdinalIgnoreCase));

    private async Task LoadSourceDatabaseRelationshipsAsync()
    {
        SourceDatabaseRelationships.Clear();
        var databaseName = SelectedDatabaseName();
        if (string.IsNullOrWhiteSpace(databaseName) || SelectedDatabaseId is null || BaseViews.Count == 0)
        {
            return;
        }

        var selectedTableNames = BaseViews
            .Select(item => item.RelationshipTableName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var registryRelationships = (await DatabaseRelationshipRepository.GetForDatabaseAsync(SelectedDatabaseId.Value))
            .Where(relationship =>
                relationship.Columns.Count > 0 &&
                selectedTableNames.Contains(relationship.SourceTableName) &&
                selectedTableNames.Contains(relationship.TargetTableName))
            .Select(ToForeignKeyEdge)
            .ToList();

        if (registryRelationships.Count > 0)
        {
            SourceDatabaseRelationships = registryRelationships;
            return;
        }

        SourceDatabaseRelationships = (await TableSchemaRepository.GetRelationshipsBetweenTablesAsync(
            databaseName,
            selectedTableNames))
            .ToList();
    }

    private async Task RefreshInferredJoinsAsync()
    {
        if (NonAnchorSelectedBaseViews.Count == 0)
        {
            StatusMessage = "Select at least two base views before inferring joins.";
            return;
        }

        IsBusy = true;

        try
        {
            await LoadSourceDatabaseRelationshipsAsync();
            var inferredCount = ApplyRelationshipDefaultsToJoinRows(overwriteInferred: true);
            GeneratedSql = string.Empty;
            StatusMessage = inferredCount == 0
                ? "No source database relationships matched the current selected base views."
                : $"Joins are inferred from source database relationships. Please check {inferredCount} join box{(inferredCount == 1 ? string.Empty : "es")} before generating.";
        }
        catch (Exception ex)
        {
            NotifyFailure("Relationship inference failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private int ApplyRelationshipDefaultsToJoinRows(bool overwriteInferred)
    {
        var anchor = AnchorView;
        if (anchor is null)
        {
            return 0;
        }

        var inferredCount = 0;
        foreach (var item in NonAnchorSelectedBaseViews)
        {
            var row = FindJoinRow(item.SchemaObjectId);
            if (row is null || (!overwriteInferred && !string.IsNullOrWhiteSpace(row.OnClause)))
            {
                continue;
            }

            var relationship = FindRelationshipBetween(anchor.RelationshipTableName, item.RelationshipTableName)
                ?? SelectedBaseViews
                    .Where(candidate => !ReferenceEquals(candidate, item))
                    .Select(candidate => FindRelationshipBetween(candidate.RelationshipTableName, item.RelationshipTableName))
                    .FirstOrDefault(match => match is not null);
            if (relationship is null)
            {
                continue;
            }

            row.OnClause = BuildJoinCondition(relationship, item);
            row.JoinType = "LEFT JOIN";
            row.IsInferred = true;
            inferredCount++;
        }

        return inferredCount;
    }

    private SchemaStudioWebViewer.Data.TableSchemaForeignKeyEdge? FindRelationshipBetween(string leftTableName, string rightTableName) =>
        SourceDatabaseRelationships.FirstOrDefault(relationship =>
            (string.Equals(relationship.ParentTableName, leftTableName, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(relationship.ReferencedTableName, rightTableName, StringComparison.OrdinalIgnoreCase)) ||
            (string.Equals(relationship.ParentTableName, rightTableName, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(relationship.ReferencedTableName, leftTableName, StringComparison.OrdinalIgnoreCase)));

    private string BuildJoinCondition(SchemaStudioWebViewer.Data.TableSchemaForeignKeyEdge relationship, DomainBaseViewItem joinedItem)
    {
        var parent = SelectedBaseViews.FirstOrDefault(item =>
            string.Equals(item.RelationshipTableName, relationship.ParentTableName, StringComparison.OrdinalIgnoreCase));
        var referenced = SelectedBaseViews.FirstOrDefault(item =>
            string.Equals(item.RelationshipTableName, relationship.ReferencedTableName, StringComparison.OrdinalIgnoreCase));

        if (parent is null || referenced is null)
        {
            return string.Empty;
        }

        return string.Join($"{Environment.NewLine}AND ", relationship.Columns.Select(column =>
            $"{QuoteIdentifier(referenced.AliasName)}.{QuoteIdentifier(column.ReferencedColumnName)} = {QuoteIdentifier(parent.AliasName)}.{QuoteIdentifier(column.ParentColumnName)}"));
    }

    private static SchemaStudioWebViewer.Data.TableSchemaForeignKeyEdge ToForeignKeyEdge(DatabaseRelationshipDefinition relationship) =>
        new(
            relationship.SourceConstraintName ?? $"{relationship.SourceTableName}_{relationship.TargetTableName}",
            relationship.SourceSchemaName,
            relationship.SourceTableName,
            relationship.TargetSchemaName,
            relationship.TargetTableName,
            relationship.Columns
                .OrderBy(column => column.OrdinalPosition)
                .Select(column => new SchemaStudioWebViewer.Data.TableSchemaForeignKeyEdgeColumn(
                    column.SourceColumnName,
                    column.TargetColumnName))
                .ToList());

    private static string BuildDefaultAlias(string sourceObjectName)
    {
        var token = SanitizeIdentifierToken(sourceObjectName);
        if (token.StartsWith("bv", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
        {
            token = token[2..];
        }

        return string.IsNullOrWhiteSpace(token) ? "SourceObject" : token;
    }

    private static string SanitizeIdentifierToken(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return chars.Length == 0 ? "Object" : new string(chars);
    }

    private static string ResolveRelationshipTableName(SchemaObjectDefinition source, string? viewNameFilter)
    {
        if (!string.IsNullOrWhiteSpace(source.SourceTableName))
        {
            return source.SourceTableName.Trim();
        }

        return StripConfiguredViewPrefix(source.SourceObjectName, viewNameFilter);
    }

    private static string StripConfiguredViewPrefix(string sourceObjectName, string? viewNameFilter)
    {
        var name = sourceObjectName.Trim();
        var prefix = NormalizeViewNamePrefix(viewNameFilter);
        if (!string.IsNullOrWhiteSpace(prefix) &&
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[prefix.Length..];
        }

        return name.TrimStart('_');
    }

    private static string NormalizeViewNamePrefix(string? viewNameFilter)
    {
        if (string.IsNullOrWhiteSpace(viewNameFilter))
        {
            return string.Empty;
        }

        var prefix = viewNameFilter.Trim().TrimEnd('%');
        return prefix;
    }

    private string GetConfiguredViewNamePrefix() =>
        NormalizeViewNamePrefix(SelectedDatabase?.ViewNameFilter);

    private void EnsureTargetViewNamePrefix()
    {
        var prefix = GetConfiguredViewNamePrefix();
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(TargetViewName))
        {
            return;
        }

        var trimmed = TargetViewName.Trim();
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            TargetViewName = trimmed;
            return;
        }

        TargetViewName = $"{prefix}{trimmed.TrimStart('_')}";
    }
}

