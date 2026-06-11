using SchemaStudio.AIHelpers;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

[FileVersion("1.3")]
[AIFileContext("SchemaStudio.Data/Models/DatabaseDefinition.cs", "Defines the editable database metadata model used by the web data layer and maintenance forms.", LastReviewed = "2026-04-23")]
[AIChange("1.0", "2026-04-23 12:56 PM CDT added ViewNameFilter metadata so databases can define a view-name include pattern for integration view selection.", AICommandStatus.Pending)]
// 2026-04-23 12:56 PM CDT AI v1.0 database-filter marker: database metadata now includes a view-name filter value for integration view list trimming.
public sealed class DatabaseDefinition
{
    [Display(AutoGenerateField = false)]
    public int DatabaseId { get; set; }

    [Required]
    [StringLength(128)]
    [Display(Name = "Database Name", Order = 10)]
    [Description("Physical SQL Server database name.")]
    public string DatabaseName { get; set; } = "";

    [Required]
    [StringLength(128)]
    [Display(Name = "Default Schema", Order = 20)]
    [Description("Default schema used when object metadata does not specify one.")]
    public string DefaultSchema { get; set; } = "dbo";

    [Required]
    [StringLength(128)]
    [Display(Name = "Business Name", Order = 30)]
    [Description("Business-facing name for this database.")]
    public string BusinessName { get; set; } = "";

    [Required]
    [StringLength(500)]
    [Display(Name = "Business Description", Order = 40)]
    [Description("Business summary for this database.")]
    public string BusinessDescription { get; set; } = "";

    [StringLength(4000)]
    [Display(Name = "Developer Notes", Order = 50)]
    [Description("Technical notes for maintainers and developers.")]
    public string? DeveloperNotes { get; set; }

    [StringLength(256)]
    [Display(Name = "View Name Filter", Order = 60)]
    [Description("Optional pattern or expression used to include only matching source view names for this database.")]
    public string? ViewNameFilter { get; set; }

    [StringLength(500)]
    [Display(Name = "SQL Query Lookup String Template", Order = 65)]
    [Description("Optional SQL template run against the source database to discover lookup relationship candidates for a loaded source table. Supported tokens include [database], [schema], [table], and [column].")]
    public string? SQLLookupString { get; set; }

    [Display(Name = "Active", Order = 70)]
    public bool Active { get; set; } = true;
}

public sealed class DatabaseLookupRelationshipDefinition
{
    [Display(AutoGenerateField = false)]
    public int DatabaseLookupRelationshipId { get; set; }

    [Display(AutoGenerateField = false)]
    public int DatabaseId { get; set; }

    [Required]
    [StringLength(128)]
    [Display(Name = "Source Schema", Order = 10)]
    public string SourceSchemaName { get; set; } = "dbo";

    [Required]
    [StringLength(128)]
    [Display(Name = "Source Table", Order = 20)]
    public string SourceTableName { get; set; } = "";

    [Required]
    [StringLength(128)]
    [Display(Name = "Source Column", Order = 30)]
    public string SourceColumnName { get; set; } = "";

    [Required]
    [StringLength(128)]
    [Display(Name = "Lookup Schema", Order = 40)]
    public string LookupSchemaName { get; set; } = "dbo";

    [Required]
    [StringLength(128)]
    [Display(Name = "Lookup Table", Order = 50)]
    public string LookupTableName { get; set; } = "";

    [Required]
    [StringLength(128)]
    [Display(Name = "Lookup Key Column", Order = 60)]
    public string LookupKeyColumnName { get; set; } = "";

    [StringLength(128)]
    [Display(Name = "Lookup Display Column", Order = 70)]
    public string? LookupDisplayColumnName { get; set; }

    [StringLength(128)]
    [Display(Name = "Lookup Filter Column", Order = 80)]
    public string? LookupFilterColumnName { get; set; }

    [StringLength(128)]
    [Display(Name = "Lookup Filter Value", Order = 90)]
    public string? LookupFilterValue { get; set; }

    [StringLength(1500)]
    [Display(Name = "Lookup Values", Order = 95)]
    [Description("Optional newline-delimited legal lookup values in [Lookup Column] = value format.")]
    public string? LookupValues { get; set; }

    [Required]
    [StringLength(32)]
    [Display(Name = "Join Type", Order = 100)]
    public string JoinType { get; set; } = "LEFT JOIN";

    [Required]
    [StringLength(32)]
    [Display(Name = "Relationship Role", Order = 110)]
    [Description("Semantic role for this relationship, such as Lookup, ParentReference, SystemOfRecord, or Ignore.")]
    public string RelationshipRole { get; set; } = "Lookup";

    [StringLength(128)]
    [Display(Name = "Relationship Name", Order = 120)]
    public string? RelationshipName { get; set; }

    [Display(Name = "Active", Order = 130)]
    public bool Active { get; set; } = true;
}
