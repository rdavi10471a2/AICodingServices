using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

public sealed class SqlObjectDependencyRepository
{
    private readonly string _connectionString;

    public SqlObjectDependencyRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<SqlObjectDependency>> GetWhereUsedAsync(
        string databaseName,
        string schemaName,
        string objectName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }

        if (string.IsNullOrWhiteSpace(schemaName))
        {
            throw new ArgumentException("Schema name is required.", nameof(schemaName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException("Object name is required.", nameof(objectName));
        }

        await using (var connection = new SqlConnection(_connectionString))
        {
            // SQL Server dependency metadata is database scoped, so the catalog database must be quoted into the query text.
            var quotedDatabase = QuoteSqlIdentifier(databaseName);
            var sql = $"""
;WITH TargetObject AS
(
    SELECT o.object_id
    FROM {quotedDatabase}.sys.objects o
    JOIN {quotedDatabase}.sys.schemas s ON o.schema_id = s.schema_id
    WHERE o.name = @objectName
      AND s.name = @schemaName
)
SELECT
    Direction = CAST('USED_BY' AS nvarchar(20)),
    DatabaseName = CAST(@databaseName AS sysname),
    SchemaName = CAST(s.name AS sysname),
    ObjectName = CAST(o.name AS sysname)
FROM {quotedDatabase}.sys.sql_expression_dependencies d
JOIN TargetObject t ON d.referenced_id = t.object_id
JOIN {quotedDatabase}.sys.objects o ON d.referencing_id = o.object_id
JOIN {quotedDatabase}.sys.schemas s ON o.schema_id = s.schema_id
ORDER BY s.name, o.name;
""";

            var rows = await connection.QueryAsync<SqlObjectDependency>(
                sql,
                new
                {
                    databaseName,
                    schemaName,
                    objectName
                });

            return rows.ToList();
        }
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }
}
