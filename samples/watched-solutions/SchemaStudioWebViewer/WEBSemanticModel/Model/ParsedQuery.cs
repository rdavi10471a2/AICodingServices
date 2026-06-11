namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    [SchemaStudio.AIHelpers.FileVersion("1.1")]
    [SchemaStudio.AIHelpers.AIFileContext("WEBSemanticModel/Model/ParsedQuery.cs", "Owns the parser result graph, validates resolved lineage, and projects SelectItems into ViewSourcedColumnDefinition records for UI workflows.", Responsibilities = "Carries DisableInheritance and Semantic* through the parsed-query projection step so parser-tagged override flags and semantic lookup targets reach parser column output.", Nuances = "Keep physical Base* and semantic Semantic* projection explicit whenever new parser-owned fields need to survive into parsed columns.", RelatedFiles = "SelectItem, ViewMetadataBinder, ViewSourcedColumnDefinition", LastReviewed = "2026-04-28")]
    [SchemaStudio.AIHelpers.AIChange("1.1", "2026-04-28 09:47 PM CDT projected SelectItem Semantic* fields into ViewSourcedColumnDefinition so semantic lookup targets survive parser column materialization.", SchemaStudio.AIHelpers.AICommandStatus.Pending)]
    [SchemaStudio.AIHelpers.AIChange("1.0", "2026-04-25 12:18 PM CDT carried DisableInheritance through the parsed-query projection step so parser-tagged semantic override flags survive into the web save flow.", SchemaStudio.AIHelpers.AICommandStatus.Pending)]
    public class ParsedQuery
    {
        // 2026-04-28 09:47 PM CDT AI v1.1 marker: parser projection now carries Semantic* lookup targets alongside physical Base* lineage.
        // 2026-04-25 12:18 PM CDT AI v1.0 marker: parsed query projection now carries DisableInheritance into parsed view columns.
        public string SourceQuery { get; set; }

        public List<SelectItem> SelectItems { get; set; } = new();

        public List<SourceTable> SourceTables { get; set; } = new();

        //-----------------------------------------
        // 🔥 FINAL PROJECTED COLUMNS (UI / METADATA)
        //-----------------------------------------
        public List<ViewSourcedColumnDefinition> Columns { get; set; } = new();


        public List<string> ValidateColumnSemantics(string contextTable)
        {
            var errors = new List<string>();

            //-----------------------------------------
            // RULE A: Semantic + Lineage
            //-----------------------------------------
            foreach (var col in Columns)
            {
                if (string.IsNullOrWhiteSpace(col.ColumnName))
                    errors.Add("Column missing name");

                if (col.ColumnKind == ColumnKind.Simple)
                {
                    if (string.IsNullOrWhiteSpace(col.BaseTable))
                        errors.Add($"{col.ColumnName} lost lineage");

                    if (col.BaseTable == contextTable)
                        errors.Add($"{col.ColumnName} incorrectly resolves to view");

                    if (string.IsNullOrWhiteSpace(col.BaseColumn))
                        errors.Add($"{col.ColumnName} missing base column");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(col.BaseTable) &&
                        !col.BaseTable.Equals(contextTable, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"{col.ColumnName} expression leaked lineage ({col.BaseTable})");
                    }
                }
            }

            //-----------------------------------------
            // RULE B: Coverage (your addition)
            //-----------------------------------------
            var usedTables = Columns
                .Where(c => c.ColumnKind == ColumnKind.Simple)
                .Select(c => c.BaseTable)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var declaredTables = SourceTables
                .Select(t => t.Table)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var t in usedTables)
            {
                if (!declaredTables.Contains(t))
                    errors.Add($"Lineage references table not in source graph: {t}");
            }

            //-----------------------------------------
            // OPTIONAL: sanity
            //-----------------------------------------
            if (!declaredTables.Any())
                errors.Add("No source tables discovered");

            return errors;
        }

        //-----------------------------------------
        // 🔥 PROJECTION STEP
        //-----------------------------------------
        public List<ViewSourcedColumnDefinition> ToColumns(
            string contextDatabase,
            string contextSchema,
            string contextTable)
        {
            var result = new List<ViewSourcedColumnDefinition>();

            for (int i = 0; i < SelectItems.Count; i++)
            {
                var item = SelectItems[i];

                var col = new ViewSourcedColumnDefinition
                {
                    //-----------------------------------------
                    // ORDER
                    //-----------------------------------------
                    OrdinalPosition = i + 1,

                    //-----------------------------------------
                    // OUTPUT NAME (The Alias)
                    // 🔥 In s.CustomerID AS Customer_ID, this is "Customer_ID"
                    //-----------------------------------------
                    ColumnName = item.Alias,

                    //-----------------------------------------
                    // 🔥 PROJECTION OWNER (VIEW)
                    //-----------------------------------------
                    Database = contextDatabase,
                    Schema = contextSchema,
                    Table = contextTable,

                    //-----------------------------------------
                    // 🔥 BASE LINEAGE
                    // 🔥 In s.CustomerID AS Customer_ID, BaseColumn is "CustomerID"
                    //-----------------------------------------
                    BaseDatabase = item.BaseDatabase,
                    BaseSchema = item.BaseSchema,
                    BaseTable = item.BaseTable,
                    BaseColumn = item.BaseColumn,
                    SemanticDatabase = item.SemanticDatabase,
                    SemanticSchema = item.SemanticSchema,
                    SemanticObject = item.SemanticObject,
                    SemanticColumn = item.SemanticColumn,
                    

                    Comment = item.Comment,
                    BusinessName = item.BusinessName,
                    BusinessDescription = item.BusinessDescription,
                    DeveloperNotes = item.DeveloperNotes,
                    DisableInheritance = item.DisableInheritance,
                   // CanInheritBase = item.AllowIherit,
                    ColumnKind = item.Kind
                };

                result.Add(col);
            }

            return result;
        }
    }
}
