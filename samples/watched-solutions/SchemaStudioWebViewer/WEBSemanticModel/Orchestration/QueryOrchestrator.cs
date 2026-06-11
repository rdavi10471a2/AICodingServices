using SchemaStudioWebViewer.WEBSemanticModel.Binding;
using SchemaStudioWebViewer.WEBSemanticModel.Diagnostics;
using SchemaStudioWebViewer.WEBSemanticModel.Model;
using SchemaStudioWebViewer.WEBSemanticModel.Parsing;
using SchemaStudioWebViewer.WEBSemanticModel.Providers;
using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.WEBSemanticModel.Orchestration
{
    [FileVersion("1.1")]
    [AIFileContext("WEBSemanticModel/Orchestration/QueryOrchestrator.cs", "Coordinates full SQL view parsing, dependency expansion, column binding, view ownership assignment, and parser metadata binding before projection.", Responsibilities = "Owns the final expression ownership pass so composed columns are attributed to the view that defines them while pass-through upstream expressions keep their upstream expression owner.", Nuances = "ViewOwnershipBinder is the semantic boundary for non-simple select items; keep Base* physical/simple lineage and Semantic* lookup targets synchronized there.", RelatedFiles = "QueryBinder, ColumnBinder, ParsedQuery, SelectItem", LastReviewed = "2026-04-28")]
    [AIChange("1.1", "2026-04-28 10:00 PM CDT made ViewOwnershipBinder explicitly assign composed expression ownership to the defining view while preserving upstream expression ownership when QueryBinder identified a pass-through projected expression.", AICommandStatus.Pending)]
    [AIInstructions("2026-03-30 15:53 preserve inherited database/schema context when resolving nested view dependencies and avoid forced dbo fallback.", AICommandStatus.Pending)]
    public static class QueryOrchestrator
    {
        //-----------------------------------------
        // ENTRY POINT (LOGGER REQUIRED)
        //-----------------------------------------
        public static ParsedQuery ParseFully(
            string sql,
            string database,
            string schema,
            string viewName,
            IViewDefinitionProvider provider,
            IQueryLogger logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            logger.Info($"Orchestrator START: {database}.{schema}.{viewName}");

            #region DontEditMisionCritical

            try
            {
                //-----------------------------------------
                // STEP 1: PARSE ROOT
                //-----------------------------------------
                var parser = new ViewParser();
                parser.Logger = logger;

                var root = parser.Parse(sql);

                if (root == null)
                {
                    logger.Warning("Root parse returned null");
                    return null;
                }

                //-----------------------------------------
                // STEP 2: RESOLVE DEPENDENCIES (WITH LOOP GUARD)
                //-----------------------------------------
                ResolveDependencies(
                    root,
                    database,
                    schema,
                    provider,
                    logger,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                );

                //-----------------------------------------
                // 3. BIND DIRECT COLUMN REFERENCES
                //-----------------------------------------
                ColumnBinder.Bind(root);

                //-----------------------------------------
                // 4. PROJECT THROUGH DERIVED / VIEWS
                //-----------------------------------------
                QueryBinder.Bind(root);

                //-----------------------------------------
                // 5. ASSIGN VIEW OWNERSHIP
                //-----------------------------------------
               
                ViewOwnershipBinder.Apply(root, database, schema, viewName);
                ViewMetadataBinder.Apply(root);

                logger.Info("Orchestrator COMPLETE");

                return root;
            }
            catch (Exception ex)
            {
                logger.Error($"FAILED Orchestrator.ParseFully: {database}.{schema}.{viewName}", ex);
                throw;
            }

            #endregion
        }

        public static class ViewOwnershipBinder
        {
            public static void Apply(ParsedQuery query, string db, string schema, string objectName)
            {
                if (query == null)
                    return;

                foreach (var item in query.SelectItems)
                {
                    bool isTraceableSimpleColumn =
                        item.Kind == ColumnKind.Simple &&
                        !string.IsNullOrWhiteSpace(item.BaseTable) &&
                        !string.IsNullOrWhiteSpace(item.BaseColumn);

                    if (!isTraceableSimpleColumn)
                    {
                        AssignExpressionOwnership(item, db, schema, objectName);
                    }
                    else if (string.IsNullOrWhiteSpace(item.SemanticObject))
                    {
                        AssignSemanticDefaultFromPhysicalLineage(item);
                    }
                }
            }

            private static void AssignExpressionOwnership(SelectItem item, string db, string schema, string objectName)
            {
                if (!string.IsNullOrWhiteSpace(item.ExpressionTable) &&
                    !string.IsNullOrWhiteSpace(item.ExpressionColumn))
                {
                    AssignLineageAndSemantic(
                        item,
                        item.ExpressionDatabase,
                        item.ExpressionSchema,
                        item.ExpressionTable,
                        item.ExpressionColumn);
                    return;
                }

                // 2026-04-28 10:00 PM CDT AI v1.1 marker: composed select items are owned by the view that defines the expression, not by any one input column.
                AssignLineageAndSemantic(item, db, schema, objectName, item.Alias);
            }

            private static void AssignSemanticDefaultFromPhysicalLineage(SelectItem item)
            {
                AssignSemantic(item, item.BaseDatabase, item.BaseSchema, item.BaseTable, item.BaseColumn);
            }

            private static void AssignLineageAndSemantic(
                SelectItem item,
                string database,
                string schema,
                string objectName,
                string columnName)
            {
                item.BaseDatabase = database;
                item.BaseSchema = schema;
                item.BaseTable = objectName;
                item.BaseColumn = columnName;
                AssignSemantic(item, database, schema, objectName, columnName);
            }

            private static void AssignSemantic(
                SelectItem item,
                string database,
                string schema,
                string objectName,
                string columnName)
            {
                item.SemanticDatabase = database;
                item.SemanticSchema = schema;
                item.SemanticObject = objectName;
                item.SemanticColumn = columnName;
            }
        }

        //-----------------------------------------
        // DEPENDENCY RESOLUTION (FIXED)
        //-----------------------------------------
        private static void ResolveDependencies(
            ParsedQuery query,
            string currentDb,
            string currentSchema,
            IViewDefinitionProvider provider,
            IQueryLogger logger,
            HashSet<string> visited)
        {
            if (query == null)
                return;

            foreach (var source in query.SourceTables)
            {
                //-----------------------------------------
                // HANDLE DERIVED FIRST
                //-----------------------------------------
                if (source.NestedQuery != null)
                {
                    logger.Info($"Resolving nested query: {source.Alias}");
                    ResolveDependencies(source.NestedQuery, currentDb, currentSchema, provider, logger, visited);
                    continue;
                }

                //-----------------------------------------
                // SKIP NON-TABLE
                //-----------------------------------------
                if (string.IsNullOrWhiteSpace(source.Table))
                    continue;

                var db = source.Database ?? currentDb;
                var schema = source.Schema ?? currentSchema ?? "dbo";
                var table = source.Table;

                source.Database ??= db;
                source.Schema ??= schema;

                // 15:53 context marker: preserve inherited schema/database for unqualified nested view references.
                var key = $"{db}.{schema}.{table}".ToLowerInvariant();

                //-----------------------------------------
                // LOOP PROTECTION
                //-----------------------------------------
                if (!visited.Add(key))
                {
                    logger.Warning($"Skipping already visited: {key}");
                    continue;
                }

                logger.Info($"Resolving: {db}.{schema}.{table}");

                try
                {
                    //-----------------------------------------
                    // GET SQL (CACHE HIT/MISS HERE)
                    //-----------------------------------------
                    var sql = provider.GetViewDefinition(db, schema, table);

                    //-----------------------------------------
                    // NOT A VIEW OR NOT FOUND
                    //-----------------------------------------
                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        logger.Info($"No SQL (likely base table): {db}.{schema}.{table}");
                        continue;
                    }

                    //-----------------------------------------
                    // PARSE CHILD
                    //-----------------------------------------
                    var parser = new ViewParser();
                    parser.Logger = logger;

                    var child = parser.Parse(sql);

                    source.NestedQuery = child;

                    //-----------------------------------------
                    // RECURSE
                    //-----------------------------------------
                    ResolveDependencies(child, db, schema, provider, logger, visited);
                }
                catch (Exception ex)
                {
                    logger.Warning($"Dependency failed: {db}.{schema}.{table} | {ex.Message}");
                }
            }
        }
    }
}

