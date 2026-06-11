using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using System.ComponentModel;

namespace SchemaStudioWebViewer.Data;

[FileVersion("1.8")]
[AIFileContext("Repositories/TableSchemaSmoRepository.cs", "Reads SQL Server table metadata for the Base View Generator page.", Responsibilities = "Provides schema, table, column, and many-to-one foreign-key metadata from a selected source database without changing the configured connection string.", Nuances = "The class name is retained from the first SMO implementation, but the metadata reads use targeted sys catalog queries because SMO object hydration was too slow for interactive use.", LastReviewed = "2026-05-07")]
public sealed class TableSchemaSmoRepository
{
    private readonly string connectionString;
    private readonly TableDisplayColumnPolicy displayColumnPolicy;

    public TableSchemaSmoRepository(string connectionString, TableDisplayColumnPolicy? displayColumnPolicy = null)
    {
        this.connectionString = connectionString;
        this.displayColumnPolicy = displayColumnPolicy ?? new TableDisplayColumnPolicy();
    }

    public async Task<IReadOnlyList<string>> GetSchemasAsync(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        var database = QuoteSqlIdentifier(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var sql = $"""
SELECT
    s.name
FROM {database}.sys.schemas AS s
WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
ORDER BY s.name;
""";

        var schemas = await connection.QueryAsync<string>(sql);
        return schemas.ToList();
    }

    public async Task<IReadOnlyList<TableSchemaTableInfo>> GetTablesAsync(string databaseName, string schemaName)
    {
        ValidateDatabaseName(databaseName);
        var database = QuoteSqlIdentifier(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var sql = $"""
SELECT
    s.name AS SchemaName,
    t.name AS TableName
FROM {database}.sys.tables AS t
JOIN {database}.sys.schemas AS s
    ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
    AND s.name = @schemaName
ORDER BY t.name;
""";

        var tables = await connection.QueryAsync<TableSchemaTableInfo>(sql, new { schemaName });
        return tables.ToList();
    }

    public async Task<TableSchemaDetails> GetTableDetailsAsync(string databaseName, string schemaName, string tableName, string? lookupDiscoverySqlTemplate = null)
    {
        ValidateDatabaseName(databaseName);
        var database = QuoteSqlIdentifier(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var tableObjectId = await GetTableObjectIdAsync(connection, database, databaseName, schemaName, tableName);
        var columns = (await connection.QueryAsync<TableSchemaColumnRow>(BuildColumnsSql(database), new { tableObjectId }))
            .Select(column => new TableSchemaColumnInfo(
                column.ColumnName,
                column.DataType,
                column.IsNullable,
                column.IsPrimaryKey,
                false))
            .ToList();

        var columnByName = columns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        var relationshipRows = (await connection.QueryAsync<TableSchemaRelationshipRow>(BuildRelationshipsSql(database), new { tableObjectId })).ToList();
        var displayColumnsByObjectId = await GetDisplayColumnsByObjectIdAsync(connection, database, databaseName, relationshipRows);
        var relationships = new List<TableSchemaRelationshipInfo>();

        foreach (var group in relationshipRows.GroupBy(row => new
                 {
                     row.ForeignKeyName,
                     row.ReferencedObjectId,
                     row.ReferencedSchemaName,
                     row.ReferencedTableName
                 }))
        {
            var pairs = group
                .OrderBy(row => row.ConstraintColumnId)
                .Select(row =>
                {
                    if (columnByName.TryGetValue(row.LocalColumnName, out var localColumn))
                    {
                        localColumn.IsForeignKey = true;
                    }

                    return new TableSchemaForeignKeyColumnInfo(row.LocalColumnName, row.ReferencedColumnName);
                })
                .ToList();

            var isRequired = pairs.Count > 0 &&
                pairs.All(pair => columnByName.TryGetValue(pair.LocalColumnName, out var localColumn) && !localColumn.IsNullable);

            relationships.Add(new TableSchemaRelationshipInfo(
                group.Key.ForeignKeyName,
                group.Key.ReferencedSchemaName,
                group.Key.ReferencedTableName,
                displayColumnsByObjectId.GetValueOrDefault(group.Key.ReferencedObjectId),
                isRequired,
                isRequired ? "INNER JOIN" : "LEFT JOIN",
                pairs));
        }

        relationships.AddRange(await GetTemplateLookupRelationshipsAsync(
            connection,
            database,
            databaseName,
            schemaName,
            tableName,
            columnByName,
            relationships,
            lookupDiscoverySqlTemplate));

        var childRelationshipRows = (await connection.QueryAsync<TableSchemaChildRelationshipRow>(BuildChildRelationshipsSql(database), new { tableObjectId })).ToList();
        var childRelationships = childRelationshipRows
            .GroupBy(row => new
            {
                row.ForeignKeyName,
                row.ChildSchemaName,
                row.ChildTableName
            })
            .Select(group => new TableSchemaChildRelationshipInfo(
                group.Key.ForeignKeyName,
                group.Key.ChildSchemaName,
                group.Key.ChildTableName,
                group
                    .OrderBy(row => row.ConstraintColumnId)
                    .Select(row => new TableSchemaChildForeignKeyColumnInfo(row.ChildColumnName, row.ParentColumnName))
                    .ToList()))
            .ToList();

        return new TableSchemaDetails(databaseName, schemaName, tableName, columns, relationships, childRelationships);
    }

    public async Task<IReadOnlyList<TableSchemaForeignKeyEdge>> GetRelationshipsBetweenTablesAsync(
        string databaseName,
        IEnumerable<string> tableNames)
    {
        ValidateDatabaseName(databaseName);
        var normalizedTableNames = tableNames
            .Where(tableName => !string.IsNullOrWhiteSpace(tableName))
            .Select(tableName => tableName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedTableNames.Length == 0)
        {
            return [];
        }

        var database = QuoteSqlIdentifier(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var sql = $"""
SELECT
    fk.name AS ForeignKeyName,
    ps.name AS ParentSchemaName,
    pt.name AS ParentTableName,
    rs.name AS ReferencedSchemaName,
    rt.name AS ReferencedTableName,
    fkc.constraint_column_id AS ConstraintColumnId,
    pc.name AS ParentColumnName,
    rc.name AS ReferencedColumnName
FROM {database}.sys.foreign_keys AS fk
JOIN {database}.sys.foreign_key_columns AS fkc
    ON fkc.constraint_object_id = fk.object_id
JOIN {database}.sys.tables AS pt
    ON pt.object_id = fk.parent_object_id
JOIN {database}.sys.schemas AS ps
    ON ps.schema_id = pt.schema_id
JOIN {database}.sys.columns AS pc
    ON pc.object_id = fkc.parent_object_id
    AND pc.column_id = fkc.parent_column_id
JOIN {database}.sys.tables AS rt
    ON rt.object_id = fk.referenced_object_id
JOIN {database}.sys.schemas AS rs
    ON rs.schema_id = rt.schema_id
JOIN {database}.sys.columns AS rc
    ON rc.object_id = fkc.referenced_object_id
    AND rc.column_id = fkc.referenced_column_id
WHERE pt.name IN @tableNames
   OR rt.name IN @tableNames
ORDER BY fk.name, fkc.constraint_column_id;
""";

        var rows = (await connection.QueryAsync<TableSchemaForeignKeyEdgeRow>(
            sql,
            new { tableNames = normalizedTableNames }))
            .ToList();

        return rows
            .GroupBy(row => new
            {
                row.ForeignKeyName,
                row.ParentSchemaName,
                row.ParentTableName,
                row.ReferencedSchemaName,
                row.ReferencedTableName
            })
            .Select(group => new TableSchemaForeignKeyEdge(
                group.Key.ForeignKeyName,
                group.Key.ParentSchemaName,
                group.Key.ParentTableName,
                group.Key.ReferencedSchemaName,
                group.Key.ReferencedTableName,
                group
                    .OrderBy(row => row.ConstraintColumnId)
                    .Select(row => new TableSchemaForeignKeyEdgeColumn(row.ParentColumnName, row.ReferencedColumnName))
                    .ToList()))
            .ToList();
    }

    public async Task<string?> GetLookupValuesTextAsync(
        string databaseName,
        string lookupSchemaName,
        string lookupTableName,
        string lookupColumnName,
        string? lookupFilterColumnName,
        string? lookupFilterValue,
        int maxRows = 500)
    {
        ValidateDatabaseName(databaseName);
        ValidateIdentifier(lookupSchemaName, nameof(lookupSchemaName));
        ValidateIdentifier(lookupTableName, nameof(lookupTableName));
        ValidateIdentifier(lookupColumnName, nameof(lookupColumnName));

        if (!string.IsNullOrWhiteSpace(lookupFilterColumnName))
        {
            ValidateIdentifier(lookupFilterColumnName, nameof(lookupFilterColumnName));
        }

        var database = QuoteSqlIdentifier(databaseName);
        var schema = QuoteSqlIdentifier(lookupSchemaName);
        var table = QuoteSqlIdentifier(lookupTableName);
        var lookupColumn = QuoteSqlIdentifier(lookupColumnName);
        var topCount = Math.Clamp(maxRows, 1, 5000);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var whereClause = string.IsNullOrWhiteSpace(lookupFilterColumnName)
            ? ""
            : $"WHERE {QuoteSqlIdentifier(lookupFilterColumnName)} = @lookupFilterValue";
        var sql = $"""
SELECT DISTINCT TOP ({topCount})
    CONVERT(nvarchar(4000), {lookupColumn}) AS LookupValue
FROM {database}.{schema}.{table}
{whereClause}
ORDER BY CONVERT(nvarchar(4000), {lookupColumn});
""";

        var values = (await connection.QueryAsync<string>(sql, new { lookupFilterValue }))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => $"[{lookupColumnName}] = {value.Trim()}")
            .ToList();

        return values.Count == 0
            ? null
            : string.Join(Environment.NewLine, values);
    }

    private static async Task EnsureDatabaseExistsAsync(SqlConnection connection, string databaseName)
    {
        const string sql = "SELECT 1 FROM sys.databases WHERE name = @databaseName;";
        var exists = await connection.ExecuteScalarAsync<int?>(sql, new { databaseName });
        if (exists != 1)
        {
            throw new InvalidOperationException($"Database [{databaseName}] was not found.");
        }
    }

    private static async Task<int> GetTableObjectIdAsync(SqlConnection connection, string database, string databaseName, string schemaName, string tableName)
    {
        var sql = $"""
SELECT t.object_id
FROM {database}.sys.tables AS t
JOIN {database}.sys.schemas AS s
    ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
    AND s.name = @schemaName
    AND t.name = @tableName;
""";

        var objectId = await connection.ExecuteScalarAsync<int?>(sql, new { schemaName, tableName });
        if (objectId is null)
        {
            throw new InvalidOperationException($"Table [{databaseName}].[{schemaName}].[{tableName}] was not found.");
        }

        return objectId.Value;
    }

    private async Task<IReadOnlyDictionary<int, string>> GetDisplayColumnsByObjectIdAsync(
        SqlConnection connection,
        string database,
        string databaseName,
        IReadOnlyList<TableSchemaRelationshipRow> relationshipRows)
    {
        var referencedObjectIds = relationshipRows
            .Select(row => row.ReferencedObjectId)
            .Distinct()
            .ToList();

        if (referencedObjectIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var candidateColumnNames = displayColumnPolicy.GetCandidateColumnNames(databaseName);
        var sql = $"""
SELECT
    t.object_id AS ReferencedObjectId,
    s.name AS ReferencedSchemaName,
    t.name AS ReferencedTableName,
    c.name AS ColumnName
FROM {database}.sys.tables AS t
JOIN {database}.sys.schemas AS s
    ON s.schema_id = t.schema_id
JOIN {database}.sys.columns AS c
    ON c.object_id = t.object_id
WHERE t.object_id IN @referencedObjectIds
    AND c.name IN @candidateColumnNames;
""";

        var candidates = (await connection.QueryAsync<TableSchemaDisplayColumnCandidateRow>(
                sql,
                new { referencedObjectIds, candidateColumnNames }))
            .GroupBy(row => row.ReferencedObjectId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    var availableColumns = group.Select(row => row.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    return displayColumnPolicy
                        .GetPreferredDisplayColumns(databaseName, first.ReferencedSchemaName, first.ReferencedTableName)
                        .FirstOrDefault(availableColumns.Contains) ?? "";
                });

        return candidates
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static string BuildColumnsSql(string database)
    {
        return $"""
SELECT
    c.name AS ColumnName,
    CASE
        WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary')
            THEN CONCAT(ty.name, '(', CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length) END, ')')
        WHEN ty.name IN ('nvarchar', 'nchar')
            THEN CONCAT(ty.name, '(', CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length / 2) END, ')')
        WHEN ty.name IN ('decimal', 'numeric')
            THEN CONCAT(ty.name, '(', c.precision, ',', c.scale, ')')
        WHEN ty.name IN ('datetime2', 'datetimeoffset', 'time')
            THEN CONCAT(ty.name, '(', c.scale, ')')
        ELSE ty.name
    END AS DataType,
    CONVERT(bit, c.is_nullable) AS IsNullable,
    CONVERT(bit, CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END) AS IsPrimaryKey
FROM {database}.sys.columns AS c
JOIN {database}.sys.types AS ty
    ON ty.user_type_id = c.user_type_id
OUTER APPLY
(
    SELECT TOP (1) ic.column_id
    FROM {database}.sys.indexes AS i
    JOIN {database}.sys.index_columns AS ic
        ON ic.object_id = i.object_id
        AND ic.index_id = i.index_id
    WHERE i.object_id = c.object_id
        AND i.is_primary_key = 1
        AND ic.column_id = c.column_id
) AS pk
WHERE c.object_id = @tableObjectId
ORDER BY c.column_id;
""";
    }

    private static string BuildRelationshipsSql(string database)
    {
        return $"""
SELECT
    fk.name AS ForeignKeyName,
    rt.object_id AS ReferencedObjectId,
    rs.name AS ReferencedSchemaName,
    rt.name AS ReferencedTableName,
    fkc.constraint_column_id AS ConstraintColumnId,
    pc.name AS LocalColumnName,
    rc.name AS ReferencedColumnName
FROM {database}.sys.foreign_keys AS fk
JOIN {database}.sys.foreign_key_columns AS fkc
    ON fkc.constraint_object_id = fk.object_id
JOIN {database}.sys.columns AS pc
    ON pc.object_id = fkc.parent_object_id
    AND pc.column_id = fkc.parent_column_id
JOIN {database}.sys.tables AS rt
    ON rt.object_id = fk.referenced_object_id
JOIN {database}.sys.schemas AS rs
    ON rs.schema_id = rt.schema_id
JOIN {database}.sys.columns AS rc
    ON rc.object_id = fkc.referenced_object_id
    AND rc.column_id = fkc.referenced_column_id
WHERE fk.parent_object_id = @tableObjectId
ORDER BY fk.name, fkc.constraint_column_id;
""";
    }

    private static string BuildChildRelationshipsSql(string database)
    {
        return $"""
SELECT
    fk.name AS ForeignKeyName,
    ps.name AS ChildSchemaName,
    pt.name AS ChildTableName,
    fkc.constraint_column_id AS ConstraintColumnId,
    pc.name AS ChildColumnName,
    rc.name AS ParentColumnName
FROM {database}.sys.foreign_keys AS fk
JOIN {database}.sys.foreign_key_columns AS fkc
    ON fkc.constraint_object_id = fk.object_id
JOIN {database}.sys.tables AS pt
    ON pt.object_id = fk.parent_object_id
JOIN {database}.sys.schemas AS ps
    ON ps.schema_id = pt.schema_id
JOIN {database}.sys.columns AS pc
    ON pc.object_id = fkc.parent_object_id
    AND pc.column_id = fkc.parent_column_id
JOIN {database}.sys.columns AS rc
    ON rc.object_id = fkc.referenced_object_id
    AND rc.column_id = fkc.referenced_column_id
WHERE fk.referenced_object_id = @tableObjectId
ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id;
""";
    }

    private async Task<IReadOnlyList<TableSchemaRelationshipInfo>> GetTemplateLookupRelationshipsAsync(
        SqlConnection connection,
        string database,
        string databaseName,
        string schemaName,
        string tableName,
        IReadOnlyDictionary<string, TableSchemaColumnInfo> columnByName,
        IReadOnlyList<TableSchemaRelationshipInfo> existingRelationships,
        string? lookupDiscoverySqlTemplate)
    {
        if (string.IsNullOrWhiteSpace(lookupDiscoverySqlTemplate))
        {
            return [];
        }

        var discoverySql = BuildLookupDiscoverySql(lookupDiscoverySqlTemplate, databaseName, schemaName, tableName, "");
        if (string.IsNullOrWhiteSpace(discoverySql))
        {
            return [];
        }

        var lookupNames = (await connection.QueryAsync<string>(discoverySql))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (lookupNames.Count == 0)
        {
            return [];
        }

        var displayColumnName = await GetPreferredColumnNameAsync(
            connection,
            database,
            "dbo",
            "COLOOKUP",
            displayColumnPolicy.GetPreferredDisplayColumns(databaseName, "dbo", "COLOOKUP"));
        var lookupKeyColumnName = await GetPreferredColumnNameAsync(connection, database, "dbo", "COLOOKUP", ["Id"]);

        if (string.IsNullOrWhiteSpace(lookupKeyColumnName))
        {
            return [];
        }

        var tablePrefix = $"{tableName}_";
        var relationships = new List<TableSchemaRelationshipInfo>();
        var existingLocalColumns = existingRelationships
            .SelectMany(relationship => relationship.Columns.Select(column => column.LocalColumnName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var lookupName in lookupNames)
        {
            if (!lookupName.StartsWith(tablePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var localColumnName = lookupName[tablePrefix.Length..];
            if (!columnByName.TryGetValue(localColumnName, out var localColumn) ||
                !existingLocalColumns.Add(localColumnName))
            {
                continue;
            }

            localColumn.IsForeignKey = true;

            relationships.Add(new TableSchemaRelationshipInfo(
                $"LOOKUP_COLOOKUP_{tableName}_{localColumnName}",
                "dbo",
                "COLOOKUP",
                displayColumnName,
                !localColumn.IsNullable,
                localColumn.IsNullable ? "LEFT JOIN" : "INNER JOIN",
                [new TableSchemaForeignKeyColumnInfo(localColumnName, lookupKeyColumnName)],
                "Name",
                lookupName));
        }

        return relationships;
    }

    private static string BuildLookupDiscoverySql(string template, string databaseName, string schemaName, string tableName, string columnName)
    {
        var sql = template
            .Replace("[database]", databaseName, StringComparison.OrdinalIgnoreCase)
            .Replace("[schema]", schemaName, StringComparison.OrdinalIgnoreCase)
            .Replace("[table]", tableName, StringComparison.OrdinalIgnoreCase)
            .Replace("[column]", columnName, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (!sql.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Lookup discovery SQL must start with SELECT.");
        }

        return sql;
    }

    private static async Task<string?> GetPreferredColumnNameAsync(
        SqlConnection connection,
        string database,
        string schemaName,
        string tableName,
        IReadOnlyList<string> preferredColumnNames)
    {
        if (preferredColumnNames.Count == 0)
        {
            return null;
        }

        var sql = $"""
SELECT
    c.name
FROM {database}.sys.tables AS t
JOIN {database}.sys.schemas AS s
    ON s.schema_id = t.schema_id
JOIN {database}.sys.columns AS c
    ON c.object_id = t.object_id
WHERE s.name = @schemaName
    AND t.name = @tableName
    AND c.name IN @preferredColumnNames;
""";

        var availableColumns = (await connection.QueryAsync<string>(sql, new { schemaName, tableName, preferredColumnNames }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return preferredColumnNames.FirstOrDefault(availableColumns.Contains);
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }
    }

    private static void ValidateIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }

        if (identifier.Contains(']') || identifier.Contains('.') || identifier.Contains(';'))
        {
            throw new ArgumentException("Identifier contains unsupported characters.", parameterName);
        }
    }

    private static string QuoteSqlIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private sealed class TableSchemaColumnRow
    {
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
    }

    private sealed class TableSchemaRelationshipRow
    {
        public string ForeignKeyName { get; set; } = "";
        public int ReferencedObjectId { get; set; }
        public string ReferencedSchemaName { get; set; } = "";
        public string ReferencedTableName { get; set; } = "";
        public int ConstraintColumnId { get; set; }
        public string LocalColumnName { get; set; } = "";
        public string ReferencedColumnName { get; set; } = "";
    }

    private sealed class TableSchemaDisplayColumnCandidateRow
    {
        public int ReferencedObjectId { get; set; }
        public string ReferencedSchemaName { get; set; } = "";
        public string ReferencedTableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
    }

    private sealed class TableSchemaChildRelationshipRow
    {
        public string ForeignKeyName { get; set; } = "";
        public string ChildSchemaName { get; set; } = "";
        public string ChildTableName { get; set; } = "";
        public int ConstraintColumnId { get; set; }
        public string ChildColumnName { get; set; } = "";
        public string ParentColumnName { get; set; } = "";
    }

    private sealed class TableSchemaForeignKeyEdgeRow
    {
        public string ForeignKeyName { get; set; } = "";
        public string ParentSchemaName { get; set; } = "";
        public string ParentTableName { get; set; } = "";
        public string ReferencedSchemaName { get; set; } = "";
        public string ReferencedTableName { get; set; } = "";
        public int ConstraintColumnId { get; set; }
        public string ParentColumnName { get; set; } = "";
        public string ReferencedColumnName { get; set; } = "";
    }
}

public sealed record TableSchemaTableInfo(string SchemaName, string TableName)
{
    public string DisplayName => $"{SchemaName}.{TableName}";
}

public sealed record TableSchemaDetails(
    [property: Description("Source database that owns the table being used to generate the base view.")]
    string DatabaseName,
    [property: Description("Source schema that owns the table being used to generate the base view.")]
    string SchemaName,
    [property: Description("Source table used as the many-side table for the generated base view.")]
    string TableName,
    [property: Description("Columns found on the source table.")]
    IReadOnlyList<TableSchemaColumnInfo> Columns,
    [property: Description("Foreign-key relationships where the source table is the many-side table and the referenced table can provide lookup display values.")]
    IReadOnlyList<TableSchemaRelationshipInfo> Relationships,
    [property: Description("Foreign-key relationships where other tables reference the selected source table. These are informational and do not affect generated SQL.")]
    IReadOnlyList<TableSchemaChildRelationshipInfo> ChildRelationships);

public sealed class TableSchemaColumnInfo
{
    public TableSchemaColumnInfo(string columnName, string dataType, bool isNullable, bool isPrimaryKey, bool isForeignKey)
    {
        ColumnName = columnName;
        DataType = dataType;
        IsNullable = isNullable;
        IsPrimaryKey = isPrimaryKey;
        IsForeignKey = isForeignKey;
        Include = true;
    }

    [Description("Physical source-table column name.")]
    public string ColumnName { get; }

    [Description("SQL Server data type as reported from the selected source database.")]
    public string DataType { get; }

    [Description("Whether the source column allows NULL values.")]
    public bool IsNullable { get; }

    [Description("Whether the source column participates in the table primary key.")]
    public bool IsPrimaryKey { get; }

    [Description("Whether the source column participates in a many-to-one foreign-key relationship.")]
    public bool IsForeignKey { get; set; }

    [Description("Whether the source column should be included in the generated SELECT projection.")]
    public bool Include { get; set; }

    [Description("User-facing business name metadata to emit for this generated output column when metadata comments are included.")]
    public string BusinessName { get; set; } = "";

    [Description("User-facing business description metadata to emit for this generated output column when metadata comments are included.")]
    public string BusinessDescription { get; set; } = "";

    [Description("Generated role label used by the page to identify primary-key and many-to-one foreign-key columns.")]
    public string KeyRole =>
        IsPrimaryKey ? "PK" :
        IsForeignKey ? "FK M-to-1" :
        string.Empty;
}

public sealed class TableSchemaRelationshipInfo
{
    public TableSchemaRelationshipInfo(
        string foreignKeyName,
        string referencedSchemaName,
        string referencedTableName,
        string? displayColumnName,
        bool isRequired,
        string selectedJoinType,
        IReadOnlyList<TableSchemaForeignKeyColumnInfo> columns,
        string? lookupFilterColumnName = null,
        string? lookupFilterValue = null)
    {
        ForeignKeyName = foreignKeyName;
        ReferencedSchemaName = referencedSchemaName;
        ReferencedTableName = referencedTableName;
        DisplayColumnName = displayColumnName;
        IsRequired = isRequired;
        SelectedJoinType = selectedJoinType;
        Columns = columns;
        LookupFilterColumnName = lookupFilterColumnName;
        LookupFilterValue = lookupFilterValue;
        Include = true;
        IncludeDisplayColumn = !string.IsNullOrWhiteSpace(displayColumnName);
    }

    [Description("SQL Server foreign-key constraint name.")]
    public string ForeignKeyName { get; }

    [Description("Schema of the one-side referenced lookup table.")]
    public string ReferencedSchemaName { get; }

    [Description("Name of the one-side referenced lookup table.")]
    public string ReferencedTableName { get; }

    [Description("Display column chosen from the referenced lookup table, such as Name or Des. Blank means no lookup display column was identified.")]
    public string? DisplayColumnName { get; set; }

    [Description("Whether every local FK column is non-nullable, making an INNER JOIN a reasonable default.")]
    public bool IsRequired { get; }

    [Description("Join type used when this lookup relationship is included in generated SQL.")]
    public string SelectedJoinType { get; set; }

    [Description("Local-to-referenced column pairs that form the foreign-key relationship.")]
    public IReadOnlyList<TableSchemaForeignKeyColumnInfo> Columns { get; }

    [Description("Optional referenced-table column used as a fixed lookup filter for template-discovered lookup relationships.")]
    public string? LookupFilterColumnName { get; }

    [Description("Optional fixed lookup value paired with LookupFilterColumnName for template-discovered lookup relationships.")]
    public string? LookupFilterValue { get; }

    [Description("Whether this relationship should generate a lookup join when lookup generation is enabled.")]
    public bool Include { get; set; }

    [Description("Whether this relationship should project its selected lookup display column when lookup generation is enabled.")]
    public bool IncludeDisplayColumn { get; set; }

    [Description("User-facing business name metadata to emit for this generated lookup display column when metadata comments are included.")]
    public string DisplayBusinessName { get; set; } = "";

    [Description("User-facing business description metadata to emit for this generated lookup display column when metadata comments are included.")]
    public string DisplayBusinessDescription { get; set; } = "";

    [Description("Comma-separated local many-side foreign-key columns.")]
    public string LocalColumns => string.Join(", ", Columns.Select(column => column.LocalColumnName));

    [Description("Comma-separated referenced one-side key columns.")]
    public string ReferencedColumns => string.Join(", ", Columns.Select(column => column.ReferencedColumnName));
}

public sealed record TableSchemaForeignKeyColumnInfo(
    [property: Description("Column on the selected many-side source table.")]
    string LocalColumnName,
    [property: Description("Column on the referenced one-side lookup table.")]
    string ReferencedColumnName);

public sealed record TableSchemaForeignKeyEdge(
    string ForeignKeyName,
    string ParentSchemaName,
    string ParentTableName,
    string ReferencedSchemaName,
    string ReferencedTableName,
    IReadOnlyList<TableSchemaForeignKeyEdgeColumn> Columns);

public sealed record TableSchemaForeignKeyEdgeColumn(
    string ParentColumnName,
    string ReferencedColumnName);

public sealed class TableSchemaChildRelationshipInfo
{
    public TableSchemaChildRelationshipInfo(
        string foreignKeyName,
        string childSchemaName,
        string childTableName,
        IReadOnlyList<TableSchemaChildForeignKeyColumnInfo> columns)
    {
        ForeignKeyName = foreignKeyName;
        ChildSchemaName = childSchemaName;
        ChildTableName = childTableName;
        Columns = columns;
    }

    [Description("SQL Server foreign-key constraint name.")]
    public string ForeignKeyName { get; }

    [Description("Schema of the child table that references the selected source table.")]
    public string ChildSchemaName { get; }

    [Description("Name of the child table that references the selected source table.")]
    public string ChildTableName { get; }

    [Description("Child-to-parent column pairs that form the reverse relationship.")]
    public IReadOnlyList<TableSchemaChildForeignKeyColumnInfo> Columns { get; }
}

public sealed record TableSchemaChildForeignKeyColumnInfo(
    [property: Description("Column on the child table.")]
    string ChildColumnName,
    [property: Description("Column on the selected parent source table.")]
    string ParentColumnName);
