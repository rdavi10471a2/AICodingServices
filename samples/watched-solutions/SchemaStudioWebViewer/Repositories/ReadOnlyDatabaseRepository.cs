using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Models;

namespace SchemaStudioWebViewer.Data
{
    [FileVersion("1.0")]
    [AIFileContext("Repositories/ReadOnlyDatabaseRepository.cs", "Provides async read-only access to database metadata records for legacy web UI paths.", LastReviewed = "2026-04-23")]
    [AIChange("1.0", "2026-04-23 01:01 PM CDT added ViewNameFilter reads so legacy UI paths can use per-database integration view filtering metadata.", AICommandStatus.Pending)]
    // 2026-04-23 01:01 PM CDT AI v1.0 readonly-filter marker: read-only database queries now include the ViewNameFilter field for UI filtering behavior.
    public class ReadOnlyDatabaseRepository
    {
        private readonly string _connectionString;

        public ReadOnlyDatabaseRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<DatabaseModel>> GetAllAsync()
        {
            await using SqlConnection conn = new(_connectionString);

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
ORDER BY DatabaseName";

            var result = await conn.QueryAsync<DatabaseModel>(sql);

            return result.ToList();
        }

        public async Task<DatabaseModel?> GetByIdAsync(int databaseId)
        {
            await using SqlConnection conn = new(_connectionString);

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
WHERE DatabaseId = @databaseId";

            return await conn.QueryFirstOrDefaultAsync<DatabaseModel>(
                sql,
                new { databaseId });
        }

        public async Task<List<DatabaseDomainModel>> GetDomainsAsync(int databaseId)
        {
            await using SqlConnection conn = new(_connectionString);

            const string sql = @"
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain
FROM DatabaseDomain
WHERE DatabaseId = @databaseId
ORDER BY Domain";

            var result = await conn.QueryAsync<DatabaseDomainModel>(
                sql,
                new { databaseId });

            return result.ToList();
        }
    }
}
