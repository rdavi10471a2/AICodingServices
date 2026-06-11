using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudioWebViewer.Models;

namespace SchemaStudioWebViewer.Data
{
    public class ReadOnlySchemaObjectRepository
    {
        private readonly string _connectionString;

        public ReadOnlySchemaObjectRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<SchemaObjectModel>> GetByDatabaseAsync(int databaseId)
        {
            await using SqlConnection conn = new(_connectionString);

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
WHERE DatabaseId = @databaseId
ORDER BY SourceSchemaName, SourceObjectName";

            var result = await conn.QueryAsync<SchemaObjectModel>(
                sql,
                new { databaseId });

            return result.ToList();
        }

        public async Task<SchemaObjectModel?> GetByIdAsync(int schemaObjectId)
        {
            await using SqlConnection conn = new(_connectionString);

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
WHERE SchemaObjectId = @schemaObjectId";

            return await conn.QueryFirstOrDefaultAsync<SchemaObjectModel>(
                sql,
                new { schemaObjectId });
        }

        public async Task<List<SchemaObjectColumnModel>> GetColumnsAsync(int schemaObjectId)
        {
            await using SqlConnection conn = new(_connectionString);

            const string sql = @"
SELECT
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
ORDER BY OrdinalPosition";

            var result = await conn.QueryAsync<SchemaObjectColumnModel>(
                sql,
                new { schemaObjectId });

            return result.ToList();
        }
    }
}
