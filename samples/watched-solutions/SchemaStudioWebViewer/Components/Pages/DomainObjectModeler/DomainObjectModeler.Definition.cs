using System.Text.Json;
using Radzen;
using SchemaStudio.Data.Models;

[module: SchemaStudio.AIHelpers.AIFileContext(
    "Components/Pages/DomainObjectModeler/DomainObjectModeler.Definition.cs",
    "Composition-definition persistence slice for the Domain Object Modeler page.",
    Responsibilities = "Build the domain object composition recipe JSON and save the managed SchemaObject header that Manage Views later reconciles against the physical SQL view.",
    RelatedFiles = "Components/Pages/DomainObjectModeler/DomainObjectModeler.razor; SchemaStudio.Data/Models/SchemaObjectDefinition.cs; SchemaStudio.Data/Repositories/SchemaObjectRepository.cs",
    LastReviewed = "2026-05-13")]

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private static readonly JsonSerializerOptions CompositionJsonOptions = new()
    {
        WriteIndented = true
    };

    private bool CanSaveViewDefinition => CanGenerate;

    private string CompositionDefinitionJsonPreview =>
        SelectedBaseViews.Count == 0
            ? string.Empty
            : BuildCompositionDefinitionJson();

    private async Task SaveViewDefinitionAsync()
    {
        await SyncTargetInputsAsync();
        EnsureTargetViewNamePrefix();

        if (!CanSaveViewDefinition)
        {
            StatusMessage = "Choose an anchor and complete every join clause before saving the view definition.";
            return;
        }

        IsBusy = true;

        try
        {
            var sourceDatabaseName = ResolveComposedViewSourceDatabaseName();
            var sourceSchemaName = TargetSchema.Trim();
            var sourceObjectName = TargetViewName.Trim();
            var compositionJson = BuildCompositionDefinitionJson();
            var existing = await SchemaObjectRepository.GetBySourceAsync(
                SelectedDatabaseId!.Value,
                sourceDatabaseName,
                sourceSchemaName,
                sourceObjectName);

            if (existing is null)
            {
                existing = new SchemaObjectDefinition
                {
                    DatabaseId = SelectedDatabaseId.Value,
                    SourceDatabaseName = sourceDatabaseName,
                    SourceSchemaName = sourceSchemaName,
                    SourceObjectName = sourceObjectName,
                    IsBaseObject = false,
                    Domain = SelectedDomain,
                    BusinessName = sourceObjectName,
                    BusinessDescription = $"Composed domain object for {SelectedDomain}.",
                    CompositionDefinitionJson = compositionJson,
                    IsActive = true
                };

                await SchemaObjectRepository.CreateAsync(existing);
            }
            else
            {
                existing.DatabaseId = SelectedDatabaseId.Value;
                existing.SourceDatabaseName = sourceDatabaseName;
                existing.SourceSchemaName = sourceSchemaName;
                existing.SourceObjectName = sourceObjectName;
                existing.IsBaseObject = false;
                existing.Domain = SelectedDomain;
                existing.BusinessName = string.IsNullOrWhiteSpace(existing.BusinessName)
                    ? sourceObjectName
                    : existing.BusinessName;
                existing.BusinessDescription = string.IsNullOrWhiteSpace(existing.BusinessDescription)
                    ? $"Composed domain object for {SelectedDomain}."
                    : existing.BusinessDescription;
                existing.CompositionDefinitionJson = compositionJson;
                existing.IsActive = true;

                await SchemaObjectRepository.UpdateAsync(existing);
            }

            StatusMessage = $"Saved view definition header for {sourceSchemaName}.{sourceObjectName}. Apply the generated SQL manually, then merge columns in Manage Views.";
            NotificationService.Notify(NotificationSeverity.Success, "View definition saved", StatusMessage, 5000);
        }
        catch (Exception ex)
        {
            NotifyFailure("View definition save failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildCompositionDefinitionJson()
    {
        var anchor = AnchorView;
        var definition = new CompositionDefinition(
            1,
            SelectedDatabaseId ?? 0,
            SelectedDatabaseName(),
            SelectedDomain,
            ResolveComposedViewSourceDatabaseName(),
            TargetSchema.Trim(),
            TargetViewName.Trim(),
            anchor?.SchemaObjectId,
            anchor?.AliasName ?? string.Empty,
            SelectedBaseViews
                .Select(item =>
                {
                    var row = FindJoinRow(item.SchemaObjectId);
                    return new CompositionElementDefinition(
                        item.SchemaObjectId,
                        item.Source.SourceDatabaseName ?? string.Empty,
                        item.Source.SourceSchemaName,
                        item.Source.SourceObjectName,
                        item.RelationshipTableName,
                        item.AliasName,
                        item.CteSourceMode,
                        item.IsAnchor,
                        item.IsAnchor ? null : row?.JoinType,
                        item.IsAnchor ? null : NormalizeNullableText(row?.OnClause));
                })
                .ToList());

        return JsonSerializer.Serialize(definition, CompositionJsonOptions);
    }

    private string ResolveComposedViewSourceDatabaseName() =>
        SelectedBaseViews
            .Select(item => item.Source.SourceDatabaseName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? SelectedDatabaseName();

    private static string? NormalizeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CompositionDefinition(
        int Version,
        int DatabaseId,
        string DatabaseName,
        string Domain,
        string SourceDatabaseName,
        string SourceSchemaName,
        string SourceObjectName,
        int? AnchorSchemaObjectId,
        string AnchorAlias,
        IReadOnlyList<CompositionElementDefinition> Elements);

    private sealed record CompositionElementDefinition(
        int SchemaObjectId,
        string SourceDatabaseName,
        string SourceSchemaName,
        string SourceObjectName,
        string RelationshipTableName,
        string Alias,
        string SourceMode,
        bool IsAnchor,
        string? JoinType,
        string? OnClause);
}
