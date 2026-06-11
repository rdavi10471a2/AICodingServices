using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Data;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

[FileVersion("1.2")]
[AIFileContext(
    "Components/Pages/BaseViewCreator/BaseViewCreatorSelectionEngine.cs",
    "Builds the isolated Base View Creator selection graph from source columns and lookup relationships.",
    Responsibilities = "Keep user selection separate from derived join dependencies so lookup display projections can drive SQL generation without recursive page helpers.",
    Nuances = "The engine intentionally owns graph state only. Razor still owns UI rendering and SQL text formatting while this fork proves the cleaner plumbing.",
    RelatedFiles = "Components/Pages/BaseViewCreator/BaseViewCreator.razor; CTEEditorSample/SQLBuilderTest/Services/CteSelectionSessionEditor.cs",
    LastReviewed = "2026-05-12")]
public sealed class BaseViewCreatorSelectionEngine
{
    public BaseViewCreatorSelectionPlan Build(
        IReadOnlyList<TableSchemaColumnInfo> columns,
        IReadOnlyList<TableSchemaRelationshipInfo> relationships)
    {
        var selectedColumnNames = columns
            .Where(column => column.Include)
            .Select(column => column.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relationshipStates = relationships
            .Select(relationship => BuildRelationshipState(relationship, selectedColumnNames))
            .ToList();

        var activeLookupColumnNames = relationshipStates
            .Where(state => state.HasLookupDisplayProjection)
            .SelectMany(state => state.Relationship.Columns.Select(pair => pair.LocalColumnName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var baseColumns = columns
            .Where(column => column.Include && !activeLookupColumnNames.Contains(column.ColumnName))
            .ToList();

        return new BaseViewCreatorSelectionPlan(
            selectedColumnNames,
            baseColumns,
            relationshipStates,
            relationshipStates.Where(state => state.HasLookupDisplayProjection).ToList());
    }

    private static BaseViewCreatorRelationshipState BuildRelationshipState(
        TableSchemaRelationshipInfo relationship,
        IReadOnlySet<string> selectedColumnNames)
    {
        var hasDisplayColumn = !string.IsNullOrWhiteSpace(relationship.DisplayColumnName);
        var selectedLocalColumns = relationship.Columns
            .Where(pair => selectedColumnNames.Contains(pair.LocalColumnName))
            .Select(pair => pair.LocalColumnName)
            .ToList();

        var hasLookupDisplayProjection =
            relationship.Include &&
            relationship.IncludeDisplayColumn &&
            hasDisplayColumn &&
            relationship.Columns.Count > 0;

        return new BaseViewCreatorRelationshipState(
            relationship,
            hasDisplayColumn,
            selectedLocalColumns,
            hasLookupDisplayProjection);
    }
}

public sealed record BaseViewCreatorSelectionPlan(
    IReadOnlySet<string> SelectedColumnNames,
    IReadOnlyList<TableSchemaColumnInfo> BaseColumns,
    IReadOnlyList<BaseViewCreatorRelationshipState> RelationshipStates,
    IReadOnlyList<BaseViewCreatorRelationshipState> JoinDependencies)
{
    public bool IsColumnSelected(string columnName) =>
        SelectedColumnNames.Contains(columnName);

    public bool ColumnHasLookupDisplayProjection(string columnName) =>
        JoinDependencies.Any(state => state.OwnsColumn(columnName));

    public bool RelationshipHasLookupDisplayProjection(TableSchemaRelationshipInfo relationship) =>
        RelationshipStates.Any(state =>
            ReferenceEquals(state.Relationship, relationship) &&
            state.HasLookupDisplayProjection);

    public bool RelationshipOwnsProjectionColumns(TableSchemaRelationshipInfo relationship) =>
        RelationshipStates.Any(state =>
            ReferenceEquals(state.Relationship, relationship) &&
            state.HasLookupDisplayProjection);

    public bool RelationshipOwnsEditorProjectionColumns(TableSchemaRelationshipInfo relationship) =>
        RelationshipStates.Any(state =>
            ReferenceEquals(state.Relationship, relationship) &&
            state.HasDisplayColumn &&
            state.Relationship.Columns.Count > 0);

    public bool ColumnBelongsToEditorRelationship(string columnName) =>
        RelationshipStates.Any(state =>
            state.HasDisplayColumn &&
            state.Relationship.Columns.Any(pair =>
                string.Equals(pair.LocalColumnName, columnName, StringComparison.OrdinalIgnoreCase)));

    public BaseViewCreatorRelationshipState? FindRelationshipState(TableSchemaRelationshipInfo relationship) =>
        RelationshipStates.FirstOrDefault(state => ReferenceEquals(state.Relationship, relationship));
}

public sealed record BaseViewCreatorRelationshipState(
    TableSchemaRelationshipInfo Relationship,
    bool HasDisplayColumn,
    IReadOnlyList<string> SelectedLocalColumns,
    bool HasLookupDisplayProjection)
{
    public bool OwnsColumn(string columnName) =>
        SelectedLocalColumns.Any(localColumn =>
            string.Equals(localColumn, columnName, StringComparison.OrdinalIgnoreCase));
}
