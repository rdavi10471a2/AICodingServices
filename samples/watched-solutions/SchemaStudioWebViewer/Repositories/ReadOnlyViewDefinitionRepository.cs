using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudioWebViewer.Models;
using System.Text.RegularExpressions;

namespace SchemaStudioWebViewer.Data
{
    public class ReadOnlyViewDefinitionRepository
    {
        private readonly string _connectionString;

        public ReadOnlyViewDefinitionRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string CleanSqlDefinition(string rawSql)
        {
            if (string.IsNullOrWhiteSpace(rawSql))
                return string.Empty;

            string pattern = @"/\*\s*@BusinessName.*?\*/";

            return Regex.Replace(
                rawSql,
                pattern,
                string.Empty,
                RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        }

        public async Task<ViewDefinitionResult?> GetViewDefinitionAsync(
            string sourceDatabaseName,
            string sourceSchemaName,
            string sourceObjectName)
        {
            if (string.IsNullOrWhiteSpace(sourceDatabaseName))
            {
                throw new ArgumentException("Source database name is required.");
            }

            string sql = $@"
SELECT
    sm.definition AS Definition,
    v.modify_date AS ModifyDate
FROM [{sourceDatabaseName}].sys.views v
INNER JOIN [{sourceDatabaseName}].sys.schemas s
    ON v.schema_id = s.schema_id
INNER JOIN [{sourceDatabaseName}].sys.sql_modules sm
    ON v.object_id = sm.object_id
WHERE s.name = @SchemaName
AND v.name = @ObjectName";

            await using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                return await conn.QueryFirstOrDefaultAsync<ViewDefinitionResult>(
                    sql,
                    new
                    {
                        SchemaName = sourceSchemaName,
                        ObjectName = sourceObjectName
                    });
            }
        }
    }
}
