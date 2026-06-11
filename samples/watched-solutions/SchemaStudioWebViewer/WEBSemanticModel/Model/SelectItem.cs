using Microsoft.SqlServer.TransactSql.ScriptDom;

using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    [FileVersion("1.1")]
    [AIFileContext("WEBSemanticModel/Model/SelectItem.cs", "Carries parser-resolved select-item identity, physical lineage, semantic source identity, and parser-owned metadata while the SQL query is being analyzed.", Responsibilities = "Carries DisableInheritance and Semantic* fields so comment-bound semantic override flags and nearest non-physical source identity can survive later parser projection work.", Nuances = "Keep Base* as physical lineage; Semantic* is reserved for the nearest non-physical schema object/column encountered in the select chain.", RelatedFiles = "ViewMetadataBinder, ParsedQuery, ViewSourcedColumnDefinition, QueryBinder, ColumnBinder", LastReviewed = "2026-04-28")]
    [AIChange("1.1", "2026-04-28 09:41 PM CDT added SemanticDatabase, SemanticSchema, SemanticObject, and SemanticColumn carriers to SelectItem so parser binding can later preserve nearest non-physical source identity separately from physical lineage.", AICommandStatus.Pending)]
    [AIChange("1.0", "2026-04-25 12:18 PM CDT added DisableInheritance to the parser select-item model so comment-bound semantic override tags survive into the parsed column projection.", AICommandStatus.Pending)]
    public class SelectItem
    {
        // 2026-04-28 09:41 PM CDT AI v1.1 marker: SelectItem now reserves Semantic* fields for nearest non-physical schema object identity, separate from Base* physical lineage.
        // 2026-04-25 12:18 PM CDT AI v1.0 marker: parsed select items now carry DisableInheritance from SQL metadata comments.
        //-----------------------------------------
        // OUTPUT
        //-----------------------------------------
        public string Database { get; set; }

        public string Alias { get; set; }

        public string Expression { get; set; }

        public ColumnKind Kind { get; set; }

        //-----------------------------------------
        // AST HANDLE
        //-----------------------------------------
        internal ScalarExpression ExpressionNode { get; set; }

        public string ExpressionText { get; set; }

      //  public bool AllowIherit { get; set; }

        //-----------------------------------------
        // SOURCE (DIRECT BINDING)
        //-----------------------------------------
        internal ColumnBinding Binding { get; set; } = new();

        //-----------------------------------------
        // BASE LINEAGE (RESOLVED)
        //-----------------------------------------
        public string BaseDatabase { get; set; }

        public string BaseSchema { get; set; }

        public string BaseTable { get; set; }

        public string BaseColumn { get; set; }

        //-----------------------------------------
        // SEMANTIC SOURCE (RESOLVED)
        //-----------------------------------------
        public string SemanticDatabase { get; set; }

        public string SemanticSchema { get; set; }

        public string SemanticObject { get; set; }

        public string SemanticColumn { get; set; }


        public string ExpressionDatabase { get; set; }

        public string ExpressionSchema { get; set; }

        public string ExpressionTable { get; set; }

        public string ExpressionColumn { get; set; }

        public int ResolvedOrder { get; set; }


        public string Comment { get; set; }
        public string BusinessName { get; set; }
        public string BusinessDescription { get; set; }
        public string DeveloperNotes { get; set; }
        public bool DisableInheritance { get; set; }
        //-----------------------------------------
        // DISPLAY
        //-----------------------------------------
        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Alias))
                return $"{Expression} AS {Alias}";

            return Expression;
        }
    }
}
