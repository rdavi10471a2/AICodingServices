using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

[FileVersion("1.0")]
[AIFileContext("SchemaStudio.Data/Repositories/SchemaObjectRepository.cs", "Read/write repository for Schema Studio managed source object metadata.", Responsibilities = "Loads, creates, updates, deletes, and validates SchemaObject records used by Manage Views and domain-object composition workflows.", Nuances = "SourceTableName is a base-view grain hint used for relationship lookup; uniqueness checks intentionally ignore IsActive because only one base view may claim a physical source table per database.", RelatedFiles = "SchemaStudio.Data/Models/SchemaObjectDefinition.cs; Components/Pages/ManageViewsNext", LastReviewed = "2026-05-14")]
public sealed class SchemaObjectRepository
{
    private readonly string _connectionString;

    public SchemaObjectRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<SchemaObjectDefinition>> GetByDatabaseAsync(int databaseId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceTableName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
FROM dbo.SchemaObject
WHERE DatabaseId = @databaseId
ORDER BY SourceSchemaName, SourceObjectName;
""";

            var rows = await connection.QueryAsync<SchemaObjectDefinition>(sql, new { databaseId });
            return rows.Select(ClearDirty).ToList();
        }
    }

    public async Task<IReadOnlyList<SchemaObjectDefinition>> GetBaseObjectsByDatabaseAndDomainAsync(
        int databaseId,
        string domain)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceTableName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
FROM dbo.SchemaObject
WHERE DatabaseId = @databaseId
  AND IsBaseObject = 1
  AND IsActive = 1
  AND ISNULL(Domain, '') = @domain
ORDER BY SourceSchemaName, SourceObjectName;
""";

            var rows = await connection.QueryAsync<SchemaObjectDefinition>(
                sql,
                new
                {
                    databaseId,
                    domain = domain?.Trim() ?? string.Empty
                });

            return rows.Select(ClearDirty).ToList();
        }
    }

    public async Task<SchemaObjectDefinition?> GetByIdAsync(int schemaObjectId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceTableName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
FROM dbo.SchemaObject
WHERE SchemaObjectId = @schemaObjectId;
""";

            var item = await connection.QueryFirstOrDefaultAsync<SchemaObjectDefinition>(sql, new { schemaObjectId });
            item?.ClearDirty();
            return item;
        }
    }

    public async Task<SchemaObjectDefinition?> GetBySourceAsync(
        int databaseId,
        string? sourceDatabaseName,
        string sourceSchemaName,
        string sourceObjectName)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT TOP (1)
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceTableName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
FROM dbo.SchemaObject
WHERE DatabaseId = @databaseId
  AND ISNULL(SourceDatabaseName, '') = ISNULL(@sourceDatabaseName, '')
  AND SourceSchemaName = @sourceSchemaName
  AND SourceObjectName = @sourceObjectName;
""";

            var item = await connection.QueryFirstOrDefaultAsync<SchemaObjectDefinition>(
                sql,
                new
                {
                    databaseId,
                    sourceDatabaseName,
                    sourceSchemaName,
                    sourceObjectName
                });

            item?.ClearDirty();
            return item;
        }
    }

    public async Task<SchemaObjectDefinition?> GetBaseObjectBySourceTableAsync(
        int databaseId,
        string sourceTableName,
        int? excludingSchemaObjectId = null)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT TOP (1)
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceTableName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
FROM dbo.SchemaObject
WHERE DatabaseId = @databaseId
  AND IsBaseObject = 1
  AND SourceTableName = @sourceTableName
  AND (@excludingSchemaObjectId IS NULL OR SchemaObjectId <> @excludingSchemaObjectId)
ORDER BY SchemaObjectId;
""";

            var item = await connection.QueryFirstOrDefaultAsync<SchemaObjectDefinition>(
                sql,
                new
                {
                    databaseId,
                    sourceTableName = sourceTableName.Trim(),
                    excludingSchemaObjectId
                });

            item?.ClearDirty();
            return item;
        }
    }

    public async Task<int> CreateAsync(SchemaObjectDefinition model)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
INSERT INTO dbo.SchemaObject
(
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceTableName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
)
OUTPUT INSERTED.SchemaObjectId
VALUES
(
    @DatabaseId,
    @SourceDatabaseName,
    @SourceSchemaName,
    @SourceTableName,
    @SourceObjectName,
    @IsBaseObject,
    @Domain,
    @BusinessName,
    @BusinessDescription,
    @DeveloperNotes,
    @CompositionDefinitionJson,
    @IsActive,
    SYSDATETIME()
);
""";

            var id = await connection.ExecuteScalarAsync<int>(sql, model);
            model.SchemaObjectId = id;
            model.ClearDirty();
            return id;
        }
    }

    public async Task UpdateAsync(SchemaObjectDefinition model)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
UPDATE dbo.SchemaObject
SET
    DatabaseId = @DatabaseId,
    SourceDatabaseName = @SourceDatabaseName,
    SourceSchemaName = @SourceSchemaName,
    SourceTableName = @SourceTableName,
    SourceObjectName = @SourceObjectName,
    IsBaseObject = @IsBaseObject,
    Domain = @Domain,
    BusinessName = @BusinessName,
    BusinessDescription = @BusinessDescription,
    DeveloperNotes = @DeveloperNotes,
    CompositionDefinitionJson = @CompositionDefinitionJson,
    IsActive = @IsActive,
    LastSynced = SYSDATETIME()
WHERE SchemaObjectId = @SchemaObjectId;
""";

            await connection.ExecuteAsync(sql, model);
            model.ClearDirty();
        }
    }

    public async Task DeleteAsync(int schemaObjectId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
DELETE FROM dbo.SchemaObject
WHERE SchemaObjectId = @schemaObjectId;
""";

            await connection.ExecuteAsync(sql, new { schemaObjectId });
        }
    }

    public async Task SaveAllAsync(IEnumerable<SchemaObjectDefinition> models)
    {
        foreach (var model in models.Where(item => item.IsDirty || item.SchemaObjectId == 0))
        {
            if (model.SchemaObjectId == 0)
            {
                await CreateAsync(model);
            }
            else
            {
                await UpdateAsync(model);
            }
        }
    }

    private static SchemaObjectDefinition ClearDirty(SchemaObjectDefinition item)
    {
        item.ClearDirty();
        return item;
    }
}
