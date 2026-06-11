using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

public sealed class SchemaObjectColumnRepository
{
    private readonly string _connectionString;

    public SchemaObjectColumnRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<SchemaObjectColumnDefinition>> GetByObjectAsync(int schemaObjectId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
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
    COALESCE(DisableInheritance, 0) AS DisableInheritance,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    SemanticDatabase,
    SemanticSchema,
    SemanticObject,
    SemanticColumn,
    LastSynced
FROM dbo.SchemaObjectColumn
WHERE SchemaObjectId = @schemaObjectId
ORDER BY OrdinalPosition;
""";

            var rows = await connection.QueryAsync<SchemaObjectColumnDefinition>(sql, new { schemaObjectId });
            return rows.Select(ClearDirty).ToList();
        }
    }

    public async Task<SchemaObjectColumnDefinition?> GetByIdAsync(int schemaObjectColumnId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
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
    COALESCE(DisableInheritance, 0) AS DisableInheritance,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    SemanticDatabase,
    SemanticSchema,
    SemanticObject,
    SemanticColumn,
    LastSynced
FROM dbo.SchemaObjectColumn
WHERE SchemaObjectColumnId = @schemaObjectColumnId;
""";

            var item = await connection.QueryFirstOrDefaultAsync<SchemaObjectColumnDefinition>(
                sql,
                new { schemaObjectColumnId });

            item?.ClearDirty();
            return item;
        }
    }

    public async Task<int> CreateAsync(SchemaObjectColumnDefinition model)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
INSERT INTO dbo.SchemaObjectColumn
(
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
    SemanticDatabase,
    SemanticSchema,
    SemanticObject,
    SemanticColumn,
    LastSynced
)
OUTPUT INSERTED.SchemaObjectColumnId
VALUES
(
    @SchemaObjectId,
    @OrdinalPosition,
    @SourceColumnName,
    @SourceColumnKind,
    @BaseDatabaseName,
    @BaseSchemaName,
    @BaseObjectName,
    @BaseColumnName,
    @IsBaseDefinition,
    @DisableInheritance,
    @BusinessName,
    @BusinessDescription,
    @DeveloperNotes,
    @SemanticDatabase,
    @SemanticSchema,
    @SemanticObject,
    @SemanticColumn,
    SYSDATETIME()
);
""";

            var id = await connection.ExecuteScalarAsync<int>(sql, model);
            model.SchemaObjectColumnId = id;
            model.ClearDirty();
            return id;
        }
    }

    public async Task UpdateAsync(SchemaObjectColumnDefinition model)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
UPDATE dbo.SchemaObjectColumn
SET
    OrdinalPosition = @OrdinalPosition,
    SourceColumnName = @SourceColumnName,
    SourceColumnKind = @SourceColumnKind,
    BaseDatabaseName = @BaseDatabaseName,
    BaseSchemaName = @BaseSchemaName,
    BaseObjectName = @BaseObjectName,
    BaseColumnName = @BaseColumnName,
    IsBaseDefinition = @IsBaseDefinition,
    DisableInheritance = @DisableInheritance,
    BusinessName = @BusinessName,
    BusinessDescription = @BusinessDescription,
    DeveloperNotes = @DeveloperNotes,
    SemanticDatabase = @SemanticDatabase,
    SemanticSchema = @SemanticSchema,
    SemanticObject = @SemanticObject,
    SemanticColumn = @SemanticColumn,
    LastSynced = SYSDATETIME()
WHERE SchemaObjectColumnId = @SchemaObjectColumnId;
""";

            await connection.ExecuteAsync(sql, model);
            model.ClearDirty();
        }
    }

    public async Task DeleteAsync(int schemaObjectColumnId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
DELETE FROM dbo.SchemaObjectColumn
WHERE SchemaObjectColumnId = @schemaObjectColumnId;
""";

            await connection.ExecuteAsync(sql, new { schemaObjectColumnId });
        }
    }

    public async Task SaveAllAsync(IEnumerable<SchemaObjectColumnDefinition> models)
    {
        var items = models.Where(item => item.IsDirty || item.SchemaObjectColumnId == 0).ToList();
        if (items.Count == 0)
        {
            return;
        }

        await using (var connection = new SqlConnection(_connectionString))
        {
            var parameters = new DynamicParameters();
            parameters.Add("@Items", BuildUpsertTable(items).AsTableValuedParameter("dbo.SchemaObjectColumnUpsertType"));

            await connection.ExecuteAsync(
                "dbo.SchemaObjectColumn_Upsert",
                parameters,
                commandType: CommandType.StoredProcedure);
        }

        foreach (var model in items)
        {
            model.ClearDirty();
        }
    }

    public async Task SaveFullSnapshotAsync(IEnumerable<SchemaObjectColumnDefinition> models)
    {
        var items = models.ToList();
        if (items.Count == 0)
        {
            return;
        }

        await using (var connection = new SqlConnection(_connectionString))
        {
            var parameters = new DynamicParameters();
            parameters.Add("@Items", BuildUpsertTable(items).AsTableValuedParameter("dbo.SchemaObjectColumnUpsertType"));

            await connection.ExecuteAsync(
                "dbo.SchemaObjectColumn_UpsertFull",
                parameters,
                commandType: CommandType.StoredProcedure);
        }

        foreach (var model in items)
        {
            model.ClearDirty();
        }
    }

    private static DataTable BuildUpsertTable(IEnumerable<SchemaObjectColumnDefinition> models)
    {
        var data = new DataTable();
        data.Columns.Add("SchemaObjectColumnId", typeof(int));
        data.Columns.Add("SchemaObjectId", typeof(int));
        data.Columns.Add("OrdinalPosition", typeof(int));
        data.Columns.Add("SourceColumnName", typeof(string));
        data.Columns.Add("SourceColumnKind", typeof(string));
        data.Columns.Add("BaseDatabaseName", typeof(string));
        data.Columns.Add("BaseSchemaName", typeof(string));
        data.Columns.Add("BaseObjectName", typeof(string));
        data.Columns.Add("BaseColumnName", typeof(string));
        data.Columns.Add("IsBaseDefinition", typeof(bool));
        data.Columns.Add("DisableInheritance", typeof(bool));
        data.Columns.Add("BusinessName", typeof(string));
        data.Columns.Add("BusinessDescription", typeof(string));
        data.Columns.Add("DeveloperNotes", typeof(string));
        data.Columns.Add("SemanticDatabase", typeof(string));
        data.Columns.Add("SemanticSchema", typeof(string));
        data.Columns.Add("SemanticObject", typeof(string));
        data.Columns.Add("SemanticColumn", typeof(string));

        foreach (var model in models)
        {
            data.Rows.Add(
                model.SchemaObjectColumnId,
                model.SchemaObjectId,
                model.OrdinalPosition,
                model.SourceColumnName,
                model.SourceColumnKind,
                model.BaseDatabaseName,
                model.BaseSchemaName,
                model.BaseObjectName,
                model.BaseColumnName,
                model.IsBaseDefinition,
                model.DisableInheritance,
                model.BusinessName,
                model.BusinessDescription,
                model.DeveloperNotes,
                model.SemanticDatabase,
                model.SemanticSchema,
                model.SemanticObject,
                model.SemanticColumn);
        }

        return data;
    }

    private static SchemaObjectColumnDefinition ClearDirty(SchemaObjectColumnDefinition item)
    {
        item.ClearDirty();
        return item;
    }
}
