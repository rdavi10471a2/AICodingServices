using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

using System.Text;

namespace SchemaStudioWebViewer.WEBSemanticModel.Binding
{

    public static class QueryBinder
    {
        //-----------------------------------------
        // ENTRY POINT
        //-----------------------------------------
        public static void Bind(ParsedQuery query)
        {
            if (query == null) return;

            //-----------------------------------------
            // FIX: recurse on ANY NestedQuery (not just Derived)
            //-----------------------------------------
            foreach (var src in query.SourceTables)
            {
                if (src.NestedQuery != null)
                {
                    Bind(src.NestedQuery);
                }
            }

            //-----------------------------------------
            // BIND CURRENT
            //-----------------------------------------
            foreach (var item in query.SelectItems)
            {
                if (item.ExpressionNode == null)
                    continue;

                BindExpression(item, query);
            }
        }

        //-----------------------------------------
        // EXPRESSION ROUTER
        //-----------------------------------------
        private static void BindExpression(SelectItem item, ParsedQuery query)
        {
            if (item.ExpressionNode is ColumnReferenceExpression colRef)
            {
                BindColumnReference(item, colRef, query);
                return;
            }

            //-----------------------------------------
            // NON-SIMPLE
            //-----------------------------------------
            item.Kind = ColumnKind.Expression;
            item.ExpressionText = GenerateSql(item.ExpressionNode);
        }

        //-----------------------------------------
        // COLUMN REFERENCE
        //-----------------------------------------
        private static void BindColumnReference(
            SelectItem item,
            ColumnReferenceExpression colRef,
            ParsedQuery query)
        {
            var identifiers = colRef.MultiPartIdentifier?.Identifiers;

            if (identifiers == null || identifiers.Count == 0)
                return;

            string column = identifiers.Last().Value;

            string alias = null;
            string schema = null;
            string table = null;
            string database = null;

            //-----------------------------------------
            // IDENTIFIER PARSING
            //-----------------------------------------
            if (identifiers.Count == 1)
            {
                column = identifiers[0].Value;
            }
            else if (identifiers.Count == 2)
            {
                alias = identifiers[0].Value;
                column = identifiers[1].Value;
            }
            else if (identifiers.Count == 3)
            {
                schema = identifiers[0].Value;
                table = identifiers[1].Value;
                column = identifiers[2].Value;
            }
            else if (identifiers.Count == 4)
            {
                database = identifiers[0].Value;
                schema = identifiers[1].Value;
                table = identifiers[2].Value;
                column = identifiers[3].Value;
            }

            //-----------------------------------------
            // RESOLVE SOURCE
            //-----------------------------------------
            SourceTable source = null;

            if (!string.IsNullOrWhiteSpace(alias))
            {
                source = query.SourceTables
                    .FirstOrDefault(t => t.Alias == alias);
            }
            else if (!string.IsNullOrWhiteSpace(table))
            {
                source = query.SourceTables
                    .FirstOrDefault(t =>
                        t.Table == table &&
                        (schema == null || t.Schema == schema));
            }
            else
            {
                source = query.SourceTables.FirstOrDefault();
            }

            if (source == null)
                return;

            //-----------------------------------------
            // BASE TABLE
            //-----------------------------------------
            if (source.NestedQuery == null)
            {
                BindDirect(item, source, column);
                return;
            }

            //-----------------------------------------
            // FIX: recurse for ANY NestedQuery (view OR derived)
            //-----------------------------------------
            var inner = ResolveFromDerived(source.NestedQuery, column);

            if (inner != null)
            {
                if (inner.Kind != ColumnKind.Simple)
                {
                    // Preserve the immediate upstream projected expression source so later
                    // ownership/propagation logic does not have to default expressions to self.
                    item.ExpressionDatabase = source.Database;
                    item.ExpressionSchema = source.Schema;
                    item.ExpressionTable = source.Table;
                    item.ExpressionColumn = inner.Alias ?? column;
                }

                //-----------------------------------------
                // LINEAGE
                //-----------------------------------------
                item.BaseDatabase = inner.BaseDatabase;
                item.BaseSchema = inner.BaseSchema;
                item.BaseTable = inner.BaseTable;
                item.BaseColumn = inner.BaseColumn;

                ApplySemanticSource(item, source, inner, column);

                //-----------------------------------------
                // KIND
                //-----------------------------------------
                item.Kind = inner.Kind;

                //-----------------------------------------
                // EXPRESSION TEXT
                //-----------------------------------------
                if (inner.Kind == ColumnKind.Simple)
                {
                    item.ExpressionText = null;
                }
                else
                {
                    item.ExpressionText = inner.ExpressionText ?? inner.Expression;
                }

                return;
            }

            //-----------------------------------------
            // fallback
            //-----------------------------------------
            item.Kind = ColumnKind.Expression;
            item.ExpressionText = GenerateSql(item.ExpressionNode);
        }

        //-----------------------------------------
        // DIRECT BIND
        //-----------------------------------------
        private static void BindDirect(
            SelectItem item,
            SourceTable source,
            string column)
        {
            item.Binding.SourceAlias = source.Alias;
            item.Binding.SourceDatabase = source.Database;
            item.Binding.SourceSchema = source.Schema;
            item.Binding.SourceTable = source.Table;
            item.Binding.SourceColumn = column;

            item.BaseDatabase = source.Database;
            item.BaseSchema = source.Schema;
            item.BaseTable = source.Table;
            item.BaseColumn = column;

            // 2026-04-28 09:46 PM CDT AI marker: QueryBinder defaults Semantic* to physical lineage for direct sources and overwrites it when a named nested view is the semantic source.
            item.SemanticDatabase = source.Database;
            item.SemanticSchema = source.Schema;
            item.SemanticObject = source.Table;
            item.SemanticColumn = column;

            item.Kind = ColumnKind.Simple;
            item.ExpressionText = null;
        }

        private static void ApplySemanticSource(
            SelectItem item,
            SourceTable source,
            SelectItem inner,
            string requestedColumn)
        {
            if (source?.Kind == SourceKind.NamedObject &&
                source.NestedQuery != null &&
                !string.IsNullOrWhiteSpace(source.Table))
            {
                item.SemanticDatabase = source.Database;
                item.SemanticSchema = source.Schema;
                item.SemanticObject = source.Table;
                item.SemanticColumn = inner?.Alias ?? requestedColumn;
                return;
            }

            item.SemanticDatabase = inner?.SemanticDatabase ?? inner?.BaseDatabase;
            item.SemanticSchema = inner?.SemanticSchema ?? inner?.BaseSchema;
            item.SemanticObject = inner?.SemanticObject ?? inner?.BaseTable;
            item.SemanticColumn = inner?.SemanticColumn ?? inner?.BaseColumn ?? requestedColumn;
        }

        //-----------------------------------------
        // DERIVED RESOLUTION
        //-----------------------------------------
        private static SelectItem ResolveFromDerived(
            ParsedQuery nested,
            string column)
        {
            if (nested == null) return null;

            //-----------------------------------------
            // 1. Alias
            //-----------------------------------------
            var match = nested.SelectItems
                .FirstOrDefault(s =>
                    !string.IsNullOrWhiteSpace(s.Alias) &&
                    string.Equals(s.Alias, column, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match;

            //-----------------------------------------
            // 2. Column reference
            //-----------------------------------------
            match = nested.SelectItems
                .FirstOrDefault(s =>
                {
                    if (s.ExpressionNode is ColumnReferenceExpression cref)
                    {
                        var ids = cref.MultiPartIdentifier?.Identifiers;
                        return ids != null &&
                               ids.Count > 0 &&
                               string.Equals(ids.Last().Value, column, StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
                });

            if (match != null)
                return match;

            //-----------------------------------------
            // 3. Expression fallback
            //-----------------------------------------
            return nested.SelectItems
                .FirstOrDefault(s =>
                    string.Equals(s.Expression, column, StringComparison.OrdinalIgnoreCase));
        }

        //-----------------------------------------
        // SQL GENERATOR
        //-----------------------------------------
        private static string GenerateSql(TSqlFragment fragment)
        {
            var sb = new StringBuilder();

            using var writer = new StringWriter(sb);

            var gen = new Sql150ScriptGenerator(
                new SqlScriptGeneratorOptions
                {
                    KeywordCasing = KeywordCasing.Uppercase,
                    IncludeSemicolons = false
                });

            gen.GenerateScript(fragment, writer);

            return sb.ToString();
        }
    }
}

