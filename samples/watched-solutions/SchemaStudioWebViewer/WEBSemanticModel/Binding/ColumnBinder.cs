using SchemaStudioWebViewer.WEBSemanticModel.Model;
using System.Diagnostics;


//added from schemastuido
//added from winforms

namespace SchemaStudioWebViewer.WEBSemanticModel.Binding
{
    public static class ColumnBinder
    {
        public static void Bind(ParsedQuery query)
        {
            if (query == null) return;

            Debug.WriteLine("=== ColumnBinder.Bind START ===");

            // 1. RECURSE FIRST: Resolve the physical tables inside views/subqueries
            foreach (var source in query.SourceTables)
            {
                if (source.NestedQuery != null)
                {
                    Debug.WriteLine($"  → [RECURSE] Entering Nested Source: {source.Alias} ({source.Schema}.{source.Table})");
                    Bind(source.NestedQuery);
                }
            }

            // 2. BIND CURRENT LEVEL
            foreach (var item in query.SelectItems)
            {
                var col = item.Binding.SourceColumn;
                var alias = item.Binding.SourceAlias;

                Debug.WriteLine($"ITEM → Alias:[{item.Alias}] SrcAlias:[{alias}] SrcCol:[{col}]");

                if (string.IsNullOrWhiteSpace(col))
                {
                    Debug.WriteLine("    → Skip: No SourceColumn found.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(alias))
                {
                    var source = query.SourceTables
                        .FirstOrDefault(t => string.Equals(t.Alias, alias, StringComparison.OrdinalIgnoreCase));

                    Debug.WriteLine($"    → Mapping via Alias [{alias}]: {(source != null ? "SUCCESS" : "FAILED")}");
                    BindFromSource(item, source, col);
                }
                else if (query.SourceTables.Count == 1)
                {
                    Debug.WriteLine("    → Mapping via Single Source Fallback");
                    BindFromSource(item, query.SourceTables[0], col);
                }
            }
            Debug.WriteLine("=== ColumnBinder.Bind END ===");
        }

        private static void BindFromSource(SelectItem item, SourceTable source, string column)
        {
            if (source == null) return;

            Debug.WriteLine($"    → BindFromSource: Source={source.Alias} Table={source.Table} TargetCol={column}");

            // Populate current level binding
            item.Binding.SourceAlias = source.Alias;
            item.Binding.SourceDatabase = source.Database;
            item.Binding.SourceSchema = source.Schema;
            item.Binding.SourceTable = source.Table;
            item.Binding.SourceColumn = column;

            // TRAVERSE: If it's a view, find the physical column inside it
            if (source.NestedQuery != null)
            {
                Debug.WriteLine($"      → [TRAVERSE] Searching inner query of {source.Alias} for {column}");
                var innerMatch = ResolveFromDerived(source.NestedQuery, column);

                if (innerMatch != null)
                {
                    Debug.WriteLine($"      → [FOUND INNER] {innerMatch.BaseSchema}.{innerMatch.BaseTable}.{innerMatch.BaseColumn}");

                    // LIFT the physical source info to the top level
                    item.BaseDatabase = innerMatch.BaseDatabase;
                    item.BaseSchema = innerMatch.BaseSchema;
                    item.BaseTable = innerMatch.BaseTable;
                    item.BaseColumn = innerMatch.BaseColumn;
                    ApplySemanticSource(item, source, innerMatch, column);
                    item.Kind = innerMatch.Kind;

                    if (innerMatch.Kind != ColumnKind.Simple)
                        item.ExpressionText = innerMatch.ExpressionText ?? innerMatch.Expression;

                    return;
                }
                Debug.WriteLine($"      → [NOT FOUND] Column {column} not found in inner query of {source.Alias}");
            }

            // TERMINAL NODE: This is the physical table
            Debug.WriteLine($"      → [TERMINAL] Setting Base to {source.Schema}.{source.Table}");
            item.BaseDatabase = source.Database;
            item.BaseSchema = source.Schema;
            item.BaseTable = source.Table;
            item.BaseColumn = column;
            // 2026-04-28 09:47 PM CDT AI marker: ColumnBinder seeds Semantic* from physical sources so parser output has a lookup target even without an intermediate semantic view.
            item.SemanticDatabase = source.Database;
            item.SemanticSchema = source.Schema;
            item.SemanticObject = source.Table;
            item.SemanticColumn = column;
            item.Kind = ColumnKind.Simple;
        }

        private static void ApplySemanticSource(SelectItem item, SourceTable source, SelectItem inner, string requestedColumn)
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

        private static SelectItem ResolveFromDerived(ParsedQuery query, string column)
        {
            // Match Alias first, then SourceColumn fallback
            var match = query.SelectItems.FirstOrDefault(x => string.Equals(x.Alias, column, StringComparison.OrdinalIgnoreCase))
                     ?? query.SelectItems.FirstOrDefault(x => string.Equals(x.Binding.SourceColumn, column, StringComparison.OrdinalIgnoreCase));
            return match;
        }
    }
}
