using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

public sealed class DatabaseDomainRepository
{
    public const string UnknownDomainName = "Unknown";

    private readonly string _connectionString;

    public DatabaseDomainRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DatabaseDomainDefinition>> GetByDatabaseIdAsync(int databaseId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain,
    Description
FROM dbo.DatabaseDomain
WHERE DatabaseId = @databaseId
ORDER BY Domain;
""";

            var rows = await connection.QueryAsync<DatabaseDomainDefinition>(sql, new { databaseId });
            return rows.ToList();
        }
    }

    public async Task<DatabaseDomainDefinition?> GetByIdAsync(int databaseDomainId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain,
    Description
FROM dbo.DatabaseDomain
WHERE DatabaseDomainId = @databaseDomainId;
""";

            return await connection.QueryFirstOrDefaultAsync<DatabaseDomainDefinition>(sql, new { databaseDomainId });
        }
    }

    public async Task<int> CreateAsync(DatabaseDomainDefinition domain)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
INSERT INTO dbo.DatabaseDomain
(
    DatabaseId,
    Domain,
    Description
)
OUTPUT INSERTED.DatabaseDomainId
VALUES
(
    @DatabaseId,
    @Domain,
    @Description
);
""";

            var databaseDomainId = await connection.ExecuteScalarAsync<int>(sql, domain);
            domain.DatabaseDomainId = databaseDomainId;
            return databaseDomainId;
        }
    }

    public async Task UpdateAsync(DatabaseDomainDefinition domain)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
UPDATE dbo.DatabaseDomain
SET
    Domain = @Domain,
    Description = @Description
WHERE DatabaseDomainId = @DatabaseDomainId;
""";

            await connection.ExecuteAsync(sql, domain);
        }
    }

    public async Task<DatabaseDomainDeleteResult> DeleteAndReassignAsync(int databaseId, string domainName)
    {
        var normalizedDomain = domainName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDomain))
        {
            throw new InvalidOperationException("Domain name is required.");
        }

        if (string.Equals(normalizedDomain, UnknownDomainName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Unknown domain cannot be deleted.");
        }

        await using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                const string findDomainSql = """
SELECT TOP (1)
    DatabaseDomainId,
    Domain
FROM dbo.DatabaseDomain WITH (UPDLOCK, HOLDLOCK)
WHERE DatabaseId = @databaseId
  AND LTRIM(RTRIM(Domain)) = @domainName;
""";

                var deletedDomain = await connection.QueryFirstOrDefaultAsync<DatabaseDomainDefinition>(
                    findDomainSql,
                    new { databaseId, domainName = normalizedDomain },
                    transaction);

                if (deletedDomain is null)
                {
                    await transaction.RollbackAsync();
                    throw new InvalidOperationException("The selected domain no longer exists.");
                }

                const string findUnknownSql = """
SELECT TOP (1)
    DatabaseDomainId,
    Domain
FROM dbo.DatabaseDomain WITH (UPDLOCK, HOLDLOCK)
WHERE DatabaseId = @databaseId
  AND LTRIM(RTRIM(Domain)) = @unknownDomain;
""";

                var unknownDomain = await connection.QueryFirstOrDefaultAsync<DatabaseDomainDefinition>(
                    findUnknownSql,
                    new { databaseId, unknownDomain = UnknownDomainName },
                    transaction);

                if (unknownDomain is null)
                {
                    const string createUnknownSql = """
INSERT INTO dbo.DatabaseDomain
(
    DatabaseId,
    Domain,
    Description
)
OUTPUT INSERTED.DatabaseDomainId
VALUES
(
    @databaseId,
    @unknownDomain,
    @description
);
""";

                    var unknownDomainId = await connection.ExecuteScalarAsync<int>(
                        createUnknownSql,
                        new
                        {
                            databaseId,
                            unknownDomain = UnknownDomainName,
                            description = "Fallback domain for objects reassigned after a domain is deleted."
                        },
                        transaction);

                    unknownDomain = new DatabaseDomainDefinition
                    {
                        DatabaseDomainId = unknownDomainId,
                        DatabaseId = databaseId,
                        Domain = UnknownDomainName
                    };
                }

                const string reassignObjectsSql = """
UPDATE dbo.SchemaObject
SET
    Domain = @unknownDomain,
    LastSynced = SYSDATETIME()
WHERE DatabaseId = @databaseId
  AND LTRIM(RTRIM(ISNULL(Domain, ''))) = @deletedDomain;
""";

                var reassignedObjectCount = await connection.ExecuteAsync(
                    reassignObjectsSql,
                    new
                    {
                        databaseId,
                        unknownDomain = unknownDomain.Domain,
                        deletedDomain = deletedDomain.Domain.Trim()
                    },
                    transaction);

                const string deleteDomainSql = """
DELETE FROM dbo.DatabaseDomain
WHERE DatabaseDomainId = @databaseDomainId;
""";

                await connection.ExecuteAsync(
                    deleteDomainSql,
                    new { databaseDomainId = deletedDomain.DatabaseDomainId },
                    transaction);

                await transaction.CommitAsync();

                return new DatabaseDomainDeleteResult(
                    deletedDomain.Domain,
                    unknownDomain.Domain,
                    reassignedObjectCount);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    public async Task DeleteAsync(int databaseDomainId)
    {
        var domain = await GetByIdAsync(databaseDomainId)
            ?? throw new InvalidOperationException("The selected domain no longer exists.");

        await DeleteAndReassignAsync(domain.DatabaseId, domain.Domain);
    }
}

public sealed record DatabaseDomainDeleteResult(
    string DeletedDomain,
    string ReassignedDomain,
    int ReassignedObjectCount);
