using Microsoft.Data.SqlClient;
using SchemaStudioWebViewer.WEBSemanticModel.Diagnostics;
using SchemaStudioWebViewer.WEBSemanticModel.Model;
using SchemaStudioWebViewer.WEBSemanticModel.Parsing;
using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.WEBSemanticModel.Providers
{
    //-----------------------------------------
    // IMPLEMENT INTERFACE
    //-----------------------------------------
    [FileVersion("1.0")]
    [AIInstructions("2026-03-30 15:53 preserve inherited schema context when collecting view dependencies for cache invalidation and nested resolution.", AICommandStatus.Pending)]
    public class ViewDefinitionProvider : IViewDefinitionProvider
    {
        private readonly string _connectionString;
        private readonly IQueryLogger _logger;

        private readonly Dictionary<string, string> _sqlCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly object _cacheLock = new object();

        public ViewDefinitionProvider(string connectionString, IQueryLogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is required.", nameof(connectionString));
            }

            _connectionString = connectionString;
            _logger = logger ?? new NullQueryLogger();

            _logger.Info("ViewDefinitionProvider initialized");
        }

        //-----------------------------------------
        // CLEAR ALL CACHE
        //-----------------------------------------
        public void ClearCache()
        {
            _logger.Info("Cache CLEAR ALL");

            lock (_cacheLock)
            {
                _sqlCache.Clear();
            }
        }

        //-----------------------------------------
        // RELOAD SINGLE VIEW
        //-----------------------------------------
        public void Reload(string dbName, string schema, string viewName)
        {
            if (string.IsNullOrWhiteSpace(dbName) ||
                string.IsNullOrWhiteSpace(viewName))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(schema))
            {
                schema = "dbo";
            }

            string key = dbName + "." + schema + "." + viewName;

            _logger.Info($"Cache REMOVE: {key}");

            lock (_cacheLock)
            {
                _sqlCache.Remove(key);
            }
        }

        //-----------------------------------------
        // RELOAD CHAIN
        //-----------------------------------------
        public void ReloadChain(string dbName, string schema, string viewName)
        {
            if (string.IsNullOrWhiteSpace(dbName) ||
                string.IsNullOrWhiteSpace(viewName))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(schema))
            {
                schema = "dbo";
            }

            _logger.Info($"ReloadChain START: {dbName}.{schema}.{viewName}");

            //-----------------------------------------
            // STEP 1: UNCACHED FETCH
            //-----------------------------------------
            string sql = GetViewDefinitionUncached(dbName, schema, viewName);

            if (string.IsNullOrWhiteSpace(sql))
            {
                _logger.Warning("ReloadChain: root SQL not found");
                return;
            }

            //-----------------------------------------
            // STEP 2: PARSE ROOT
            //-----------------------------------------
            ViewParser parser = new ViewParser();
            parser.Logger = _logger;

            ParsedQuery root = parser.Parse(sql);

            if (root == null)
            {
                _logger.Warning("ReloadChain: parse returned null");
                return;
            }

            //-----------------------------------------
            // STEP 3: COLLECT DEPENDENCIES
            //-----------------------------------------
            HashSet<string> dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectDependencies(root, dbName, schema, dependencies);

            _logger.Info($"ReloadChain dependencies found: {dependencies.Count}");

            //-----------------------------------------
            // STEP 4: REMOVE FROM CACHE
            //-----------------------------------------
            lock (_cacheLock)
            {
                string rootKey = dbName + "." + schema + "." + viewName;

                _sqlCache.Remove(rootKey);
                _logger.Info($"Cache REMOVE: {rootKey}");

                foreach (string dep in dependencies)
                {
                    string[] parts = dep.Split('|');

                    if (parts.Length == 3)
                    {
                        string key = parts[0] + "." + parts[1] + "." + parts[2];
                        _sqlCache.Remove(key);

                        _logger.Info($"Cache REMOVE: {key}");
                    }
                }
            }

            _logger.Info("ReloadChain COMPLETE");
        }

        //-----------------------------------------
        // UNCACHED FETCH
        //-----------------------------------------
        private string GetViewDefinitionUncached(string dbName, string schema, string viewName)
        {
            string dbSafe = "[" + dbName.Replace("]", "]]") + "]";

            _logger.Info($"DB FETCH: {dbName}.{schema}.{viewName}");

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
@"
SELECT m.definition
FROM " + dbSafe + @".sys.sql_modules m
JOIN " + dbSafe + @".sys.objects o ON m.object_id = o.object_id
JOIN " + dbSafe + @".sys.schemas s ON o.schema_id = s.schema_id
WHERE o.name = @viewName
  AND s.name = @schema";

                    cmd.Parameters.AddWithValue("@viewName", viewName);
                    cmd.Parameters.AddWithValue("@schema", schema);

                    object result = cmd.ExecuteScalar();

                    if (result == null)
                    {
                        _logger.Warning("DB FETCH returned NULL");
                        return null;
                    }

                    return result as string;
                }
            }
        }

        //-----------------------------------------
        // GET VIEW (CACHED)
        //-----------------------------------------
        public string GetViewDefinition(string dbName, string schema, string viewName)
        {
            if (string.IsNullOrWhiteSpace(dbName) ||
                string.IsNullOrWhiteSpace(viewName))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(schema))
            {
                schema = "dbo";
            }

            string cacheKey = dbName + "." + schema + "." + viewName;

            //-----------------------------------------
            // CACHE CHECK
            //-----------------------------------------
            lock (_cacheLock)
            {
                string cached;

                if (_sqlCache.TryGetValue(cacheKey, out cached))
                {
                    _logger.Info($"CACHE HIT: {cacheKey}");
                    return cached;
                }
            }

            //-----------------------------------------
            // MISS → FETCH
            //-----------------------------------------
            _logger.Info($"CACHE MISS: {cacheKey}");

            string sql = GetViewDefinitionUncached(dbName, schema, viewName);

            //-----------------------------------------
            // STORE (DISABLED BY YOU)
            //-----------------------------------------
            lock (_cacheLock)
            {
                _sqlCache[cacheKey] = sql;
            }

            return sql;
        }

        //-----------------------------------------
        // GET VIEW LIST
        //-----------------------------------------
        public List<string> GetViews(string dbName, string schema)
        {
            if (string.IsNullOrWhiteSpace(dbName))
            {
                return new List<string>();
            }

            if (string.IsNullOrWhiteSpace(schema))
            {
                schema = "dbo";
            }

            _logger.Info($"GetViews List: {dbName}.{schema}");

            string dbSafe = "[" + dbName.Replace("]", "]]") + "]";

            List<string> results = new List<string>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
@"
SELECT o.name
FROM " + dbSafe + @".sys.objects o
JOIN " + dbSafe + @".sys.schemas s ON o.schema_id = s.schema_id
WHERE o.type = 'V'
  AND s.name = @schema
ORDER BY o.name";

                    cmd.Parameters.AddWithValue("@schema", schema);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(reader.GetString(0));
                        }
                    }
                }
            }

            _logger.Info($"GetViews returned {results.Count} items");

            return results;
        }

        //-----------------------------------------
        // DEPENDENCY WALKER
        //-----------------------------------------
        private void CollectDependencies(
            ParsedQuery query,
            string viewDB,
            string viewSchema,
            HashSet<string> results)
        {
            if (query == null)
            {
                return;
            }

            foreach (SourceTable source in query.SourceTables)
            {
                if (string.IsNullOrWhiteSpace(source.Table))
                {
                    continue;
                }

                string schema = source.Schema ?? viewSchema ?? "dbo";
                string db = source.Database ?? viewDB;

                source.Database ??= db;
                source.Schema ??= schema;

                // 15:53 cache marker: inherit the current schema before tracking dependent views.
                string key = db + "|" + schema + "|" + source.Table;

                results.Add(key);

                if (source.NestedQuery != null)
                {
                    CollectDependencies(source.NestedQuery, db, schema, results);
                }
            }
        }
    }
}

