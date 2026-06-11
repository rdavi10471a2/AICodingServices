using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

[FileVersion("1.1")]
[AIFileContext("SchemaStudio.Data/Repositories/SourceViewRepository.cs", "Reads source SQL view definitions from a selected database so the manage-views workspace can show available import candidates filtered by database-maintained rules.", LastReviewed = "2026-04-23")]
[AIChange("1.0", "2026-04-23 01:29 PM CDT added source-view discovery queries with optional ViewNameFilter support for the new manage-views workspace.", AICommandStatus.Pending)]
// 2026-04-23 01:29 PM CDT AI v1.0 source-view-repo marker: available source views can now be queried from the selected database with optional name filtering.
public sealed class SourceViewRepository
{
    private readonly string _connectionString;

    public SourceViewRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<SourceViewDefinition>> GetByDatabaseAsync(string databaseName, string? viewNameFilter)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }

        await using (var connection = new SqlConnection(_connectionString))
        {
            var quotedDatabase = QuoteSqlIdentifier(databaseName);
            var hasFilter = !string.IsNullOrWhiteSpace(viewNameFilter);
            var normalizedFilter = NormalizeStartsWithFilter(viewNameFilter);

            var sql = $"""
SELECT
    DatabaseName = CAST(@databaseName AS sysname),
    SchemaName = CAST(s.name AS sysname),
    ObjectName = CAST(v.name AS sysname),
    ModifyDate = v.modify_date
FROM {quotedDatabase}.sys.views v
INNER JOIN {quotedDatabase}.sys.schemas s
    ON v.schema_id = s.schema_id
WHERE (@hasFilter = 0 OR v.name LIKE @normalizedFilter ESCAPE '\')
ORDER BY s.name, v.name;
""";

            var rows = await connection.QueryAsync<SourceViewDefinition>(
                sql,
                new
                {
                    databaseName,
                    hasFilter,
                    normalizedFilter
                });

            return rows.ToList();
        }
    }

    private static string NormalizeStartsWithFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "%";
        }

        var prefix = value.Trim().TrimEnd('%');
        return EscapeLike(prefix) + "%";
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }
}
