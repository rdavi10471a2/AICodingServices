using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

[FileVersion("1.4")]
[AIFileContext("SchemaStudio.Data/Repositories/DatabaseRepository.cs", "Read/write repository for Schema Studio database metadata records.", Responsibilities = "Loads and maintains dbo.Databases rows for maintenance screens and downstream schema tools.", Nuances = "Applies small additive metadata table upgrades before database reads and writes so UI fields can roll out without a separate migration step.", LastReviewed = "2026-05-11")]
public sealed class DatabaseRepository
{
    private readonly string _connectionString;

    public DatabaseRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DatabaseDefinition>> GetAllAsync()
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    SQLLookupString,
    Active
FROM dbo.Databases
ORDER BY DatabaseName;
""";

            var rows = await connection.QueryAsync<DatabaseDefinition>(sql);
            return rows.ToList();
        }
    }

    public async Task<DatabaseDefinition?> GetByIdAsync(int databaseId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    SQLLookupString,
    Active
FROM dbo.Databases
WHERE DatabaseId = @databaseId;
""";

            return await connection.QueryFirstOrDefaultAsync<DatabaseDefinition>(sql, new { databaseId });
        }
    }

    public async Task<int> CreateAsync(DatabaseDefinition database)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
INSERT INTO dbo.Databases
(
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    SQLLookupString,
    Active
)
OUTPUT INSERTED.DatabaseId
VALUES
(
    @DatabaseName,
    @DefaultSchema,
    @BusinessName,
    @BusinessDescription,
    @DeveloperNotes,
    @ViewNameFilter,
    @SQLLookupString,
    @Active
);
""";

            var databaseId = await connection.ExecuteScalarAsync<int>(sql, database);
            database.DatabaseId = databaseId;
            return databaseId;
        }
    }

    public async Task UpdateAsync(DatabaseDefinition database)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
UPDATE dbo.Databases
SET
    DatabaseName = @DatabaseName,
    DefaultSchema = @DefaultSchema,
    BusinessName = @BusinessName,
    BusinessDescription = @BusinessDescription,
    DeveloperNotes = @DeveloperNotes,
    ViewNameFilter = @ViewNameFilter,
    SQLLookupString = @SQLLookupString,
    Active = @Active
WHERE DatabaseId = @DatabaseId;
""";

            await connection.ExecuteAsync(sql, database);
        }
    }

    public async Task DeleteAsync(int databaseId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
DELETE FROM dbo.Databases
WHERE DatabaseId = @databaseId;
""";

            await connection.ExecuteAsync(sql, new { databaseId });
        }
    }

    private static async Task EnsureDatabaseMetadataColumnsAsync(SqlConnection connection)
    {
        const string sql = """
IF COL_LENGTH('dbo.Databases', 'SQLLookupString') IS NULL
BEGIN
    ALTER TABLE dbo.Databases
        ADD SQLLookupString nvarchar(500) NULL;
END;
""";

        await connection.ExecuteAsync(sql);
    }
}

public sealed class DatabaseLookupRelationshipRepository
{
    private readonly string _connectionString;
    private readonly string _metadataDatabaseName;

    public DatabaseLookupRelationshipRepository(string connectionString)
    {
        _connectionString = connectionString;
        _metadataDatabaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }

    private string LookupRelationshipsTable => $"{QuoteSqlIdentifier(_metadataDatabaseName)}.dbo.DatabaseLookupRelationships";

    public async Task<IReadOnlyList<DatabaseLookupRelationshipDefinition>> GetBySourceAsync(int databaseId, string sourceSchemaName, string sourceTableName)
    {
        await using var connection = new SqlConnection(_connectionString);
        if (!await HasTableAsync(connection, "DatabaseLookupRelationships"))
        {
            return [];
        }

        var lookupValuesProjection = await HasColumnAsync(connection, "DatabaseLookupRelationships", "LookupValues")
            ? "LookupValues,"
            : "CAST(NULL AS nvarchar(1500)) AS LookupValues,";
        var relationshipRoleProjection = await HasColumnAsync(connection, "DatabaseLookupRelationships", "RelationshipRole")
            ? "RelationshipRole,"
            : "CAST(N'Lookup' AS nvarchar(32)) AS RelationshipRole,";

        var sql = $"""
SELECT
    DatabaseLookupRelationshipId,
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    SourceColumnName,
    LookupSchemaName,
    LookupTableName,
    LookupKeyColumnName,
    LookupDisplayColumnName,
    LookupFilterColumnName,
    LookupFilterValue,
    {lookupValuesProjection}
    JoinType,
    {relationshipRoleProjection}
    RelationshipName,
    Active
FROM {LookupRelationshipsTable}
WHERE DatabaseId = @databaseId
    AND SourceSchemaName = @sourceSchemaName
    AND SourceTableName = @sourceTableName
ORDER BY SourceColumnName, LookupSchemaName, LookupTableName, LookupFilterValue;
""";

        var rows = await connection.QueryAsync<DatabaseLookupRelationshipDefinition>(
            sql,
            new { databaseId, sourceSchemaName, sourceTableName });
        return rows.ToList();
    }

    public async Task<int> CreateIfMissingAsync(DatabaseLookupRelationshipDefinition relationship)
    {
        Normalize(relationship);

        await using var connection = new SqlConnection(_connectionString);
        await EnsureLookupRelationshipTableExistsAsync(connection);

        var sql = $"""
DECLARE @ExistingId int;

SELECT TOP (1)
    @ExistingId = DatabaseLookupRelationshipId
FROM {LookupRelationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND SourceSchemaName = @SourceSchemaName
    AND SourceTableName = @SourceTableName
    AND SourceColumnName = @SourceColumnName
    AND LookupSchemaName = @LookupSchemaName
    AND LookupTableName = @LookupTableName
    AND LookupKeyColumnName = @LookupKeyColumnName
    AND ISNULL(LookupFilterColumnName, N'') = ISNULL(@LookupFilterColumnName, N'')
    AND ISNULL(LookupFilterValue, N'') = ISNULL(@LookupFilterValue, N'');

IF @ExistingId IS NOT NULL
BEGIN
    SELECT @ExistingId;
    RETURN;
END;

INSERT INTO {LookupRelationshipsTable}
(
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    SourceColumnName,
    LookupSchemaName,
    LookupTableName,
    LookupKeyColumnName,
    LookupDisplayColumnName,
    LookupFilterColumnName,
    LookupFilterValue,
    LookupValues,
    JoinType,
    RelationshipRole,
    RelationshipName,
    Active
)
OUTPUT INSERTED.DatabaseLookupRelationshipId
VALUES
(
    @DatabaseId,
    @SourceSchemaName,
    @SourceTableName,
    @SourceColumnName,
    @LookupSchemaName,
    @LookupTableName,
    @LookupKeyColumnName,
    @LookupDisplayColumnName,
    @LookupFilterColumnName,
    @LookupFilterValue,
    @LookupValues,
    @JoinType,
    @RelationshipRole,
    @RelationshipName,
    @Active
);
""";

        var id = await connection.ExecuteScalarAsync<int>(sql, relationship);
        relationship.DatabaseLookupRelationshipId = id;
        return id;
    }

    public async Task<int> UpsertAsync(DatabaseLookupRelationshipDefinition relationship)
    {
        Normalize(relationship);

        await using var connection = new SqlConnection(_connectionString);
        await EnsureLookupRelationshipTableExistsAsync(connection);

        var sql = $"""
DECLARE @ExistingId int;

SELECT TOP (1)
    @ExistingId = DatabaseLookupRelationshipId
FROM {LookupRelationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND SourceSchemaName = @SourceSchemaName
    AND SourceTableName = @SourceTableName
    AND SourceColumnName = @SourceColumnName
    AND LookupSchemaName = @LookupSchemaName
    AND LookupTableName = @LookupTableName
    AND LookupKeyColumnName = @LookupKeyColumnName
    AND ISNULL(LookupFilterColumnName, N'') = ISNULL(@LookupFilterColumnName, N'')
    AND ISNULL(LookupFilterValue, N'') = ISNULL(@LookupFilterValue, N'');

IF @ExistingId IS NOT NULL
BEGIN
    UPDATE {LookupRelationshipsTable}
    SET LookupDisplayColumnName = @LookupDisplayColumnName,
        LookupValues = @LookupValues,
        JoinType = @JoinType,
        RelationshipRole = @RelationshipRole,
        RelationshipName = @RelationshipName,
        Active = @Active
    WHERE DatabaseLookupRelationshipId = @ExistingId;

    SELECT @ExistingId;
    RETURN;
END;

INSERT INTO {LookupRelationshipsTable}
(
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    SourceColumnName,
    LookupSchemaName,
    LookupTableName,
    LookupKeyColumnName,
    LookupDisplayColumnName,
    LookupFilterColumnName,
    LookupFilterValue,
    LookupValues,
    JoinType,
    RelationshipRole,
    RelationshipName,
    Active
)
OUTPUT INSERTED.DatabaseLookupRelationshipId
VALUES
(
    @DatabaseId,
    @SourceSchemaName,
    @SourceTableName,
    @SourceColumnName,
    @LookupSchemaName,
    @LookupTableName,
    @LookupKeyColumnName,
    @LookupDisplayColumnName,
    @LookupFilterColumnName,
    @LookupFilterValue,
    @LookupValues,
    @JoinType,
    @RelationshipRole,
    @RelationshipName,
    @Active
);
""";

        var id = await connection.ExecuteScalarAsync<int>(sql, relationship);
        relationship.DatabaseLookupRelationshipId = id;
        return id;
    }

    private static void Normalize(DatabaseLookupRelationshipDefinition relationship)
    {
        relationship.SourceSchemaName = NormalizeRequired(relationship.SourceSchemaName, "Source schema");
        relationship.SourceTableName = NormalizeRequired(relationship.SourceTableName, "Source table");
        relationship.SourceColumnName = NormalizeRequired(relationship.SourceColumnName, "Source column");
        relationship.LookupSchemaName = NormalizeRequired(relationship.LookupSchemaName, "Lookup schema");
        relationship.LookupTableName = NormalizeRequired(relationship.LookupTableName, "Lookup table");
        relationship.LookupKeyColumnName = NormalizeRequired(relationship.LookupKeyColumnName, "Lookup key column");
        relationship.LookupDisplayColumnName = NormalizeOptional(relationship.LookupDisplayColumnName);
        relationship.LookupFilterColumnName = NormalizeOptional(relationship.LookupFilterColumnName);
        relationship.LookupFilterValue = NormalizeOptional(relationship.LookupFilterValue);
        relationship.LookupValues = NormalizeOptional(relationship.LookupValues);
        relationship.RelationshipName = NormalizeOptional(relationship.RelationshipName);
        relationship.JoinType = string.Equals(relationship.JoinType?.Trim(), "INNER JOIN", StringComparison.OrdinalIgnoreCase)
            ? "INNER JOIN"
            : "LEFT JOIN";
        relationship.RelationshipRole = NormalizeRelationshipRole(relationship.RelationshipRole);
    }

    private static string NormalizeRequired(string? value, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeRelationshipRole(string? value)
    {
        var role = value?.Trim();
        return role?.ToUpperInvariant() switch
        {
            "PARENTREFERENCE" => "ParentReference",
            "SYSTEMOFRECORD" => "SystemOfRecord",
            "IGNORE" => "Ignore",
            _ => "Lookup"
        };
    }

    private async Task EnsureLookupRelationshipTableExistsAsync(SqlConnection connection)
    {
        if (!await HasTableAsync(connection, "DatabaseLookupRelationships"))
        {
            throw new InvalidOperationException($"{_metadataDatabaseName}.dbo.DatabaseLookupRelationships was not found. Legacy lookup relationships have been replaced by DatabaseRelationships.");
        }
    }

    private async Task<bool> HasTableAsync(SqlConnection connection, string tableName)
    {
        var sql = $"""
SELECT COUNT(1)
FROM {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.objects AS o
JOIN {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.schemas AS s
    ON s.schema_id = o.schema_id
WHERE s.name = N'dbo'
    AND o.name = @tableName
    AND o.type = N'U';
""";

        return await connection.ExecuteScalarAsync<int>(sql, new { tableName }) > 0;
    }

    private async Task<bool> HasColumnAsync(SqlConnection connection, string tableName, string columnName)
    {
        var sql = $"""
SELECT COUNT(1)
FROM {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.columns AS c
JOIN {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.objects AS o
    ON o.object_id = c.object_id
JOIN {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.schemas AS s
    ON s.schema_id = o.schema_id
WHERE s.name = N'dbo'
    AND o.name = @tableName
    AND c.name = @columnName;
""";

        return await connection.ExecuteScalarAsync<int>(sql, new { tableName, columnName }) > 0;
    }

    private static string QuoteSqlIdentifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
}
