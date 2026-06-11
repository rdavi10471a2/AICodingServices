using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Models;
using System.Text.RegularExpressions;

namespace SchemaStudioWebViewer.Data;

[FileVersion("1.6")]
[AIFileContext("Repositories/SchemaMCPRepository.cs", "Read-only Dapper repository for MCP schema discovery tools. Provides the database/domain/object/field lookup chain used by MCP tool wrappers and the Tool Lab debug page.", Responsibilities = "Owns read-only Schema Studio metadata queries for AI-facing schema discovery without exposing write operations.", Nuances = "Keep this repository query-focused and async; tool wrappers own exception-to-tool-response conversion so failures stay structured for AI callers.", LastReviewed = "2026-05-07")]
public sealed class SchemaMCPRepository
{
    private static readonly Regex MetadataCommentRegex = new(
        @"/\*\s*@BusinessName.*?\*/",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string connectionString;

    public SchemaMCPRepository(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DatabaseModel>> GetDatabasesAsync(int top = 100, string? search = null)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT TOP (@top)
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    Active
FROM Databases
WHERE Active = 1
AND (
    @search IS NULL
    OR DatabaseName LIKE @search ESCAPE '\'
    OR BusinessName LIKE @search ESCAPE '\'
    OR BusinessDescription LIKE @search ESCAPE '\'
)
ORDER BY DatabaseName";

        var rows = await connection.QueryAsync<DatabaseModel>(
            sql,
            new
            {
                top,
                search = ToLikeSearch(search)
            });
        return rows.ToList();
    }

    public async Task<DatabaseModel?> GetDatabaseAsync(int databaseId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    Active
FROM Databases
WHERE DatabaseId = @databaseId
AND Active = 1";

        return await connection.QueryFirstOrDefaultAsync<DatabaseModel>(sql, new { databaseId });
    }

    public async Task<IReadOnlyList<DatabaseDomainModel>> GetDomainsAsync(int databaseId, int top = 100, string? search = null)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT TOP (@top)
    DatabaseDomainId,
    DatabaseId,
    Domain
FROM DatabaseDomain
WHERE DatabaseId = @databaseId
AND (
    @search IS NULL
    OR Domain LIKE @search ESCAPE '\'
)
ORDER BY Domain";

        var rows = await connection.QueryAsync<DatabaseDomainModel>(
            sql,
            new
            {
                databaseId,
                top,
                search = ToLikeSearch(search)
            });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SchemaObjectModel>> GetSchemaObjectsAsync(int databaseId, string? domain, int top = 100, string? search = null)
    {
        var result = await GetSchemaObjectListAsync(databaseId, domain, top, search);
        return result.Objects;
    }

    public async Task<SchemaObjectListResult> GetSchemaObjectListAsync(int databaseId, string? domain, int top = 100, string? search = null)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
DECLARE @normalizedDomain nvarchar(255) = NULL;

IF @domain IS NOT NULL
BEGIN
    SELECT TOP (1)
        @normalizedDomain = Domain
    FROM DatabaseDomain
    WHERE DatabaseId = @databaseId
    AND LOWER(Domain) = LOWER(@domain);
END;

SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    Active
FROM Databases
WHERE DatabaseId = @databaseId
AND Active = 1;

SELECT
    CAST(CASE WHEN @domain IS NULL OR @normalizedDomain IS NOT NULL THEN 1 ELSE 0 END AS bit) AS DomainFound,
    @normalizedDomain AS Domain;

SELECT TOP (@top)
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
FROM SchemaObject
WHERE DatabaseId = @databaseId
AND IsActive = 1
AND (@domain IS NULL OR @normalizedDomain IS NOT NULL)
AND (@normalizedDomain IS NULL OR Domain = @normalizedDomain)
AND (
    @search IS NULL
    OR SourceDatabaseName LIKE @search ESCAPE '\'
    OR SourceSchemaName LIKE @search ESCAPE '\'
    OR SourceObjectName LIKE @search ESCAPE '\'
    OR Domain LIKE @search ESCAPE '\'
    OR BusinessName LIKE @search ESCAPE '\'
    OR BusinessDescription LIKE @search ESCAPE '\'
)
ORDER BY Domain, IsBaseObject DESC, COALESCE(BusinessName, SourceObjectName), SourceSchemaName, SourceObjectName";

        var normalizedDomainInput = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim();
        var reader = await connection.QueryMultipleAsync(
            sql,
            new
            {
                databaseId,
                domain = normalizedDomainInput,
                top,
                search = ToLikeSearch(search)
            });

        var database = await reader.ReadFirstOrDefaultAsync<DatabaseModel>();
        var domainResult = await reader.ReadFirstAsync<DomainResolutionResult>();
        var objects = (await reader.ReadAsync<SchemaObjectModel>()).ToList();

        return new SchemaObjectListResult(database, domainResult.DomainFound, domainResult.Domain, objects);
    }

    public async Task<SchemaObjectModel?> GetSchemaObjectAsync(int schemaObjectId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    CompositionDefinitionJson,
    IsActive,
    LastSynced
FROM SchemaObject
WHERE SchemaObjectId = @schemaObjectId
AND IsActive = 1";

        return await connection.QueryFirstOrDefaultAsync<SchemaObjectModel>(sql, new { schemaObjectId });
    }

    public async Task<IReadOnlyList<SchemaObjectColumnModel>> GetFieldsAsync(int schemaObjectId, int top = 100, string? search = null)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT TOP (@top)
    SchemaObjectColumnId,
    SchemaObjectId,
    OrdinalPosition,
    SourceColumnName,
    SourceColumnKind,
    BaseDatabaseName,
    BaseSchemaName,
    BaseObjectName,
    BaseColumnName,
    IsBaseDefinition,
    DisableInheritance,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    LastSynced
FROM SchemaObjectColumn
WHERE SchemaObjectId = @schemaObjectId
AND (
    @search IS NULL
    OR SourceColumnName LIKE @search ESCAPE '\'
    OR SourceColumnKind LIKE @search ESCAPE '\'
    OR BaseObjectName LIKE @search ESCAPE '\'
    OR BaseColumnName LIKE @search ESCAPE '\'
    OR BusinessName LIKE @search ESCAPE '\'
    OR BusinessDescription LIKE @search ESCAPE '\'
)
ORDER BY OrdinalPosition, SourceColumnName";

        var rows = await connection.QueryAsync<SchemaObjectColumnModel>(
            sql,
            new
            {
                schemaObjectId,
                top,
                search = ToLikeSearch(search)
            });
        return rows.ToList();
    }

    public async Task<SchemaObjectColumnModel?> GetFieldAsync(int schemaObjectColumnId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    SchemaObjectColumn.SchemaObjectColumnId,
    SchemaObjectColumn.SchemaObjectId,
    SchemaObjectColumn.OrdinalPosition,
    SchemaObjectColumn.SourceColumnName,
    SchemaObjectColumn.SourceColumnKind,
    SchemaObjectColumn.BaseDatabaseName,
    SchemaObjectColumn.BaseSchemaName,
    SchemaObjectColumn.BaseObjectName,
    SchemaObjectColumn.BaseColumnName,
    SchemaObjectColumn.IsBaseDefinition,
    SchemaObjectColumn.DisableInheritance,
    SchemaObjectColumn.BusinessName,
    SchemaObjectColumn.BusinessDescription,
    SchemaObjectColumn.DeveloperNotes,
    SchemaObjectColumn.LastSynced
FROM SchemaObjectColumn
INNER JOIN SchemaObject
    ON SchemaObjectColumn.SchemaObjectId = SchemaObject.SchemaObjectId
WHERE SchemaObjectColumn.SchemaObjectColumnId = @schemaObjectColumnId
AND SchemaObject.IsActive = 1";

        return await connection.QueryFirstOrDefaultAsync<SchemaObjectColumnModel>(sql, new { schemaObjectColumnId });
    }

    public async Task<ViewDefinitionResult?> GetViewSqlAsync(int schemaObjectId)
    {
        var schemaObject = await GetSchemaObjectAsync(schemaObjectId);
        if (schemaObject is null)
        {
            return null;
        }

        return await GetViewSqlAsync(schemaObject);
    }

    public async Task<ViewDefinitionResult?> GetViewSqlAsync(SchemaObjectModel schemaObject)
    {
        if (string.IsNullOrWhiteSpace(schemaObject.SourceDatabaseName))
        {
            throw new ArgumentException("Schema object does not have a source database name.");
        }

        if (string.IsNullOrWhiteSpace(schemaObject.SourceSchemaName))
        {
            throw new ArgumentException("Schema object does not have a source schema name.");
        }

        if (string.IsNullOrWhiteSpace(schemaObject.SourceObjectName))
        {
            throw new ArgumentException("Schema object does not have a source object name.");
        }

        var quotedDatabase = QuoteSqlIdentifier(schemaObject.SourceDatabaseName);
        var sql = $@"
SELECT
    sm.definition AS Definition,
    v.modify_date AS ModifyDate
FROM {quotedDatabase}.sys.views v
INNER JOIN {quotedDatabase}.sys.schemas s
    ON v.schema_id = s.schema_id
INNER JOIN {quotedDatabase}.sys.sql_modules sm
    ON v.object_id = sm.object_id
WHERE s.name = @schemaName
AND v.name = @objectName";

        await using var connection = new SqlConnection(connectionString);
        return await connection.QueryFirstOrDefaultAsync<ViewDefinitionResult>(
            sql,
            new
            {
                schemaName = schemaObject.SourceSchemaName,
                objectName = schemaObject.SourceObjectName
            });
    }

    public static string CleanSqlDefinition(string rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
        {
            return string.Empty;
        }

        return MetadataCommentRegex.Replace(rawSql, string.Empty).Trim();
    }

    private static string QuoteSqlIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string? ToLikeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        return $"%{EscapeLike(search.Trim())}%";
    }

    private static string EscapeLike(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal)
            .Replace("[", @"\[", StringComparison.Ordinal);

    private sealed record DomainResolutionResult(bool DomainFound, string? Domain);
}

public sealed record SchemaObjectListResult(
    DatabaseModel? Database,
    bool DomainFound,
    string? Domain,
    IReadOnlyList<SchemaObjectModel> Objects);
