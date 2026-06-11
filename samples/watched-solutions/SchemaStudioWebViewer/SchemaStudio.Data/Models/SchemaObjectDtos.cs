using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

public sealed class JoinKeyDto
{
    [Display(Name = "Local Column", Order = 10)]
    [Description("The local column participating in the join relationship.")]
    public string? LocalColumn { get; set; }

    [Display(Name = "Remote Expression", Order = 20)]
    [Description("The remote column or expression matched by the local column.")]
    public string? RemoteExpression { get; set; }

    [Display(Name = "Cardinality", Order = 30)]
    [Description("The inferred join cardinality for this key relationship.")]
    public string Cardinality { get; set; } = "Unknown";
}

public sealed class SourceTableDto
{
    [Display(Name = "Source Kind", Order = 10)]
    [Description("The semantic source type, such as base table, view, or derived source.")]
    public string Kind { get; set; } = "";

    [Display(Name = "Database", Order = 20)]
    [Description("The database that owns the referenced source object.")]
    public string? Database { get; set; }

    [Display(Name = "Schema", Order = 30)]
    [Description("The schema that owns the referenced source object.")]
    public string? Schema { get; set; }

    [Display(Name = "Object Name", Order = 40)]
    [Description("The table or view name used by the parsed source.")]
    public string? Table { get; set; }

    [Display(Name = "Alias", Order = 50)]
    [Description("The SQL alias used for this source within the parsed query.")]
    public string? Alias { get; set; }

    [Display(Name = "Parent Alias", Order = 60)]
    [Description("The parent alias when this source is nested beneath another source.")]
    public string? ParentAlias { get; set; }

    [Display(Name = "Join Type", Order = 70)]
    [Description("The join type used to connect this source to the rest of the query.")]
    public string? JoinType { get; set; }

    [Display(Name = "Resolved Order", Order = 80)]
    [Description("The order in which the parser resolved this source.")]
    public int ResolvedOrder { get; set; }

    [Display(Name = "Join Expression", Order = 90)]
    [Description("The join expression used to connect this source to the current query shape.")]
    [DataType(DataType.MultilineText)]
    public string? JoinExpression { get; set; }

    [Display(Name = "Base Table", Order = 100)]
    [Description("Indicates whether this source resolves directly to a base table instead of another derived query.")]
    public bool IsBaseTable { get; set; }

    [Display(Name = "Physical Source", Order = 110)]
    [Description("The fully qualified physical source name for this parser-resolved table reference.")]
    public string PhysicalSourceName =>
        string.Join(".",
            new[] { Database, Schema, Table }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

    [Display(AutoGenerateField = false)]
    public List<JoinKeyDto> JoinKeys { get; set; } = new();
}

public sealed class ViewColumnDto
{
    [Display(AutoGenerateField = false)]
    public int ColumnId { get; set; }

    [Display(AutoGenerateField = false)]
    public int TableId { get; set; }

    [Display(Name = "Sequence", Order = 10)]
    [Description("The ordinal position of the field within the parsed view definition.")]
    public int OrdinalPosition { get; set; }

    [Display(AutoGenerateField = false)]
    public string? Database { get; set; }

    [Display(AutoGenerateField = false)]
    public string? Schema { get; set; }

    [Display(AutoGenerateField = false)]
    public string? Table { get; set; }

    [Display(Name = "View Column Name", Order = 20)]
    [Description("The column name projected by the parsed view.")]
    public string? ColumnName { get; set; }

    [Display(Name = "Column Kind", Order = 25)]
    [Description("The semantic classification of the column, such as simple, aggregate, or expression.")]
    public string ColumnKind { get; set; } = "";

    [Display(Name = "Base Database", Description = "Underlying source database for the resolved source column.", AutoGenerateField = false)]
    public string? BaseDatabase { get; set; }

    [Display(Name = "Base Schema", Description = "Underlying source schema for the resolved source column.", AutoGenerateField = false)]
    public string? BaseSchema { get; set; }

    [Display(Name = "Base Object", Description = "Underlying source table or view for the resolved source column.", AutoGenerateField = false)]
    public string? BaseTable { get; set; }

    [Display(Name = "Base Column", Description = "Underlying source column for the resolved source column.", AutoGenerateField = false)]
    public string? BaseColumn { get; set; }

    [Display(Name = "Physical Lineage", Description = "The fully qualified physical source lineage for this column when lineage is available.", AutoGenerateField = true, Order = 11)]
    public string FullyQualifiedSourceColumnName =>
        SqlQualify(BaseDatabase, BaseSchema, BaseTable, BaseColumn);

    [Display(Name = "Semantic Database", Description = "Database for the parser-selected semantic lookup target.", AutoGenerateField = false)]
    public string? SemanticDatabase { get; set; }

    [Display(Name = "Semantic Schema", Description = "Schema for the parser-selected semantic lookup target.", AutoGenerateField = false)]
    public string? SemanticSchema { get; set; }

    [Display(Name = "Semantic Object", Description = "Object for the parser-selected semantic lookup target.", AutoGenerateField = false)]
    public string? SemanticObject { get; set; }

    [Display(Name = "Semantic Column", Description = "Column for the parser-selected semantic lookup target.", AutoGenerateField = false)]
    public string? SemanticColumn { get; set; }

    // 2026-04-28 09:49 PM CDT AI marker: ParserLab columns can now surface the semantic lookup target separately from the physical source.
    [Display(Name = "Semantic Source", Description = "The fully qualified semantic lookup target for this parsed column.", AutoGenerateField = true, Order = 12)]
    public string FullyQualifiedSemanticColumnName =>
        SqlQualify(SemanticDatabase, SemanticSchema, SemanticObject, SemanticColumn);

    [Display(Name = "Disable Inheritance", Order = 25)]
    [Description("Whether this parsed view column should opt out of inherited semantic metadata.")]
    public bool DisableInheritance { get; set; }

    [Display(Name = "Business Name", Order = 30)]
    [Description("Business Name for this column.")]
    public string? BusinessName { get; set; }

    [Display(Name = "Description", Order = 40)]
    [Description("Overview of the purpose of this column and what it represents.")]
    [DataType(DataType.MultilineText)]
    public string? BusinessDescription { get; set; }

    [Display(Name = "Dev Notes", Order = 50)]
    [Description("Additional notes and information about this column and details on advanced use.")]
    [DataType(DataType.MultilineText)]
    public string? DeveloperNotes { get; set; }

    [Display(Name = "Comment", Order = 60)]
    [Description("Comment text captured from the parsed SQL definition for this column.")]
    [DataType(DataType.MultilineText)]
    public string? Comment { get; set; }

    [Display(AutoGenerateField = false)]
    public DateTime LastSynced { get; set; }

    [Display(AutoGenerateField = false)]
    public bool IsDirty { get; set; }

    private static string SqlQualify(params string?[] parts) =>
        string.Join(".",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => $"[{part}]"));
}
