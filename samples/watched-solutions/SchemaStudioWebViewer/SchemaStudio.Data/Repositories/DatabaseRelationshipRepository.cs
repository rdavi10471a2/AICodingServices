using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

[FileVersion("2.1")]
[AIFileContext("SchemaStudio.Data/Repositories/DatabaseRelationshipRepository.cs", "Read/write repository for the curated database relationship registry.", Responsibilities = "Loads relationship headers with ordered column pairs and saves imported or user-curated relationships without creating or altering database objects.", Nuances = "This repository intentionally assumes dbo.DatabaseRelationships and dbo.DatabaseRelationshipColumns already exist; table creation remains a human-run script.", LastReviewed = "2026-05-13")]
public sealed class DatabaseRelationshipRepository
{
    private readonly string _connectionString;
    private readonly string _metadataDatabaseName;

    public DatabaseRelationshipRepository(string connectionString)
    {
        _connectionString = connectionString;
        _metadataDatabaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }

    public string MetadataDatabaseName => _metadataDatabaseName;

    private string RelationshipsTable => $"{QuoteSqlIdentifier(_metadataDatabaseName)}.dbo.DatabaseRelationships";

    private string RelationshipColumnsTable => $"{QuoteSqlIdentifier(_metadataDatabaseName)}.dbo.DatabaseRelationshipColumns";

    public async Task<IReadOnlyList<DatabaseRelationshipDefinition>> GetForDatabaseAsync(int databaseId)
    {
        await using var connection = new SqlConnection(_connectionString);
        var relationships = (await connection.QueryAsync<DatabaseRelationshipDefinition>(
            BuildSelectSql("DatabaseId = @databaseId"),
            new { databaseId })).ToList();

        await LoadColumnsAsync(connection, relationships, RelationshipColumnsTable);
        return relationships;
    }

    public async Task<IReadOnlyList<DatabaseRelationshipDefinition>> GetByTableAsync(
        int databaseId,
        string schemaName,
        string tableName)
    {
        await using var connection = new SqlConnection(_connectionString);
        var relationships = (await connection.QueryAsync<DatabaseRelationshipDefinition>(
            BuildSelectSql("""
DatabaseId = @databaseId
    AND SourceSchemaName = @schemaName
    AND SourceTableName = @tableName
"""),
            new { databaseId, schemaName, tableName })).ToList();

        await LoadColumnsAsync(connection, relationships, RelationshipColumnsTable);
        return relationships;
    }

    public async Task<IReadOnlyList<DatabaseRelationshipDefinition>> GetReferencingTableAsync(
        int databaseId,
        string schemaName,
        string tableName)
    {
        await using var connection = new SqlConnection(_connectionString);
        var relationships = (await connection.QueryAsync<DatabaseRelationshipDefinition>(
            BuildSelectSql("""
DatabaseId = @databaseId
    AND TargetSchemaName = @schemaName
    AND TargetTableName = @tableName
"""),
            new { databaseId, schemaName, tableName })).ToList();

        await LoadColumnsAsync(connection, relationships, RelationshipColumnsTable);
        return relationships;
    }

    public async Task<int> UpsertAsync(DatabaseRelationshipDefinition relationship)
    {
        Normalize(relationship);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var databaseRelationshipId = relationship.DatabaseRelationshipId;
            if (databaseRelationshipId == 0)
            {
                databaseRelationshipId = await FindExistingIdAsync(connection, transaction, relationship, RelationshipsTable);
            }

            if (databaseRelationshipId == 0)
            {
                databaseRelationshipId = await InsertAsync(connection, transaction, relationship, RelationshipsTable);
            }
            else
            {
                relationship.DatabaseRelationshipId = databaseRelationshipId;
                await UpdateAsync(connection, transaction, relationship, RelationshipsTable);
            }

            await ReplaceColumnsAsync(connection, transaction, databaseRelationshipId, relationship.Columns, RelationshipColumnsTable);
            await transaction.CommitAsync();

            relationship.DatabaseRelationshipId = databaseRelationshipId;
            return databaseRelationshipId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAsync(int databaseRelationshipId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            $"""
DELETE FROM {RelationshipsTable}
WHERE DatabaseRelationshipId = @databaseRelationshipId;
""",
            new { databaseRelationshipId });
    }

    private string BuildSelectSql(string whereClause) =>
        $"""
SELECT
    DatabaseRelationshipId,
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    JoinExpression,
    DiscoverySource,
    SourceConstraintName,
    IncludeLookupByDefault,
    DisplayColumnName,
    FilterColumnName,
    FilterValue,
    DeveloperNotes,
    CreatedOn,
    UpdatedOn
FROM {RelationshipsTable}
WHERE {whereClause}
ORDER BY SourceSchemaName, SourceTableName, TargetSchemaName, TargetTableName, SourceConstraintName;
""";

    private static async Task LoadColumnsAsync(
        SqlConnection connection,
        List<DatabaseRelationshipDefinition> relationships,
        string relationshipColumnsTable)
    {
        if (relationships.Count == 0)
        {
            return;
        }

        var ids = relationships.Select(relationship => relationship.DatabaseRelationshipId).ToArray();
        var columns = (await connection.QueryAsync<DatabaseRelationshipColumnDefinition>(
            $"""
SELECT
    DatabaseRelationshipColumnId,
    DatabaseRelationshipId,
    OrdinalPosition,
    SourceColumnName,
    TargetColumnName
FROM {relationshipColumnsTable}
WHERE DatabaseRelationshipId IN @ids
ORDER BY DatabaseRelationshipId, OrdinalPosition;
""",
            new { ids }))
            .GroupBy(column => column.DatabaseRelationshipId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var relationship in relationships)
        {
            relationship.Columns = columns.GetValueOrDefault(relationship.DatabaseRelationshipId) ?? new List<DatabaseRelationshipColumnDefinition>();
        }
    }

    private static async Task<int> FindExistingIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable)
    {
        if (!string.IsNullOrWhiteSpace(relationship.SourceConstraintName))
        {
            var constraintMatch = await connection.ExecuteScalarAsync<int?>(
                $"""
SELECT TOP (1) DatabaseRelationshipId
FROM {relationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND SourceSchemaName = @SourceSchemaName
    AND SourceTableName = @SourceTableName
    AND TargetSchemaName = @TargetSchemaName
    AND TargetTableName = @TargetTableName
    AND SourceConstraintName = @SourceConstraintName;
""",
                relationship,
                transaction);

            if (constraintMatch is not null)
            {
                return constraintMatch.Value;
            }
        }

        return await connection.ExecuteScalarAsync<int?>(
            $"""
SELECT TOP (1) DatabaseRelationshipId
FROM {relationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND SourceSchemaName = @SourceSchemaName
    AND SourceTableName = @SourceTableName
    AND TargetSchemaName = @TargetSchemaName
    AND TargetTableName = @TargetTableName
    AND JoinType = @JoinType
    AND JoinExpression = @JoinExpression;
""",
            relationship,
            transaction) ?? 0;
    }

    private static async Task<int> InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable)
    {
        return await connection.ExecuteScalarAsync<int>(
            $"""
INSERT INTO {relationshipsTable}
(
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    JoinExpression,
    DiscoverySource,
    SourceConstraintName,
    IncludeLookupByDefault,
    DisplayColumnName,
    FilterColumnName,
    FilterValue,
    DeveloperNotes,
    UpdatedOn
)
OUTPUT INSERTED.DatabaseRelationshipId
VALUES
(
    @DatabaseId,
    @SourceSchemaName,
    @SourceTableName,
    @TargetSchemaName,
    @TargetTableName,
    @JoinType,
    @JoinExpression,
    @DiscoverySource,
    @SourceConstraintName,
    @IncludeLookupByDefault,
    @DisplayColumnName,
    @FilterColumnName,
    @FilterValue,
    @DeveloperNotes,
    SYSDATETIME()
);
""",
            relationship,
            transaction);
    }

    private static async Task UpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable)
    {
        await connection.ExecuteAsync(
            $"""
UPDATE {relationshipsTable}
SET
    SourceSchemaName = @SourceSchemaName,
    SourceTableName = @SourceTableName,
    TargetSchemaName = @TargetSchemaName,
    TargetTableName = @TargetTableName,
    JoinType = @JoinType,
    JoinExpression = @JoinExpression,
    DiscoverySource = @DiscoverySource,
    SourceConstraintName = @SourceConstraintName,
    IncludeLookupByDefault = @IncludeLookupByDefault,
    DisplayColumnName = @DisplayColumnName,
    FilterColumnName = @FilterColumnName,
    FilterValue = @FilterValue,
    DeveloperNotes = @DeveloperNotes,
    UpdatedOn = SYSDATETIME()
WHERE DatabaseRelationshipId = @DatabaseRelationshipId;
""",
            relationship,
            transaction);
    }

    private static async Task ReplaceColumnsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int databaseRelationshipId,
        IReadOnlyList<DatabaseRelationshipColumnDefinition> columns,
        string relationshipColumnsTable)
    {
        await connection.ExecuteAsync(
            $"""
DELETE FROM {relationshipColumnsTable}
WHERE DatabaseRelationshipId = @databaseRelationshipId;
""",
            new { databaseRelationshipId },
            transaction);

        var rows = columns
            .OrderBy(column => column.OrdinalPosition)
            .Select((column, index) => new
            {
                DatabaseRelationshipId = databaseRelationshipId,
                OrdinalPosition = index + 1,
                column.SourceColumnName,
                column.TargetColumnName
            })
            .ToList();

        if (rows.Count == 0)
        {
            return;
        }

        await connection.ExecuteAsync(
            $"""
INSERT INTO {relationshipColumnsTable}
(
    DatabaseRelationshipId,
    OrdinalPosition,
    SourceColumnName,
    TargetColumnName
)
VALUES
(
    @DatabaseRelationshipId,
    @OrdinalPosition,
    @SourceColumnName,
    @TargetColumnName
);
""",
            rows,
            transaction);
    }

    private static void Normalize(DatabaseRelationshipDefinition relationship)
    {
        relationship.SourceSchemaName = NormalizeName(relationship.SourceSchemaName, "dbo");
        relationship.SourceTableName = NormalizeName(relationship.SourceTableName, "");
        relationship.TargetSchemaName = NormalizeName(relationship.TargetSchemaName, "dbo");
        relationship.TargetTableName = NormalizeName(relationship.TargetTableName, "");
        relationship.JoinType = NormalizeName(relationship.JoinType, "LEFT JOIN");
        relationship.JoinExpression = NormalizeName(relationship.JoinExpression, "");
        relationship.DiscoverySource = NormalizeName(relationship.DiscoverySource, "Manual");
        relationship.SourceConstraintName = NormalizeOptional(relationship.SourceConstraintName);
        relationship.DisplayColumnName = NormalizeOptional(relationship.DisplayColumnName);
        relationship.FilterColumnName = NormalizeOptional(relationship.FilterColumnName);
        relationship.FilterValue = NormalizeOptional(relationship.FilterValue);
        relationship.DeveloperNotes = NormalizeOptional(relationship.DeveloperNotes);

        if (IsChildDiscoverySource(relationship.DiscoverySource))
        {
            relationship.IncludeLookupByDefault = false;
        }

        foreach (var column in relationship.Columns)
        {
            column.SourceColumnName = NormalizeColumnName(column.SourceColumnName);
            column.TargetColumnName = NormalizeColumnName(column.TargetColumnName);
        }
    }

    private static bool IsChildDiscoverySource(string discoverySource) =>
        discoverySource.Contains("Child", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeColumnName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            var openBracket = trimmed.LastIndexOf('[', trimmed.Length - 1);
            if (openBracket >= 0 && openBracket < trimmed.Length - 1)
            {
                return trimmed[(openBracket + 1)..^1];
            }
        }

        var dot = trimmed.LastIndexOf('.');
        return dot >= 0 && dot < trimmed.Length - 1
            ? trimmed[(dot + 1)..].Trim('[', ']')
            : trimmed.Trim('[', ']');
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string QuoteSqlIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
