using SchemaStudio.AIHelpers;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

[FileVersion("1.0")]
[AIFileContext("SchemaStudio.Data/Models/SourceViewDefinition.cs", "Represents a source SQL view discovered from a selected database so the web workspace can list available import candidates.", LastReviewed = "2026-04-23")]
[AIChange("1.0", "2026-04-23 01:29 PM CDT added a source-view discovery model so the new manage-views workspace can list available views outside the imported metadata tables.", AICommandStatus.Pending)]
// 2026-04-23 01:29 PM CDT AI v1.0 source-view marker: available import candidates now have a typed model instead of being anonymous SQL rows.
public sealed class SourceViewDefinition
{
    [Display(Name = "Database", Order = 10)]
    [Description("Database containing the available source view.")]
    public string DatabaseName { get; set; } = "";

    [Display(Name = "Schema", Order = 20)]
    [Description("Schema containing the available source view.")]
    public string SchemaName { get; set; } = "";

    [Display(Name = "Object", Order = 30)]
    [Description("SQL Server view name available for import.")]
    public string ObjectName { get; set; } = "";

    [Display(Name = "Modified", Order = 40)]
    [Description("Most recent SQL Server modify timestamp for the source view.")]
    public DateTime? ModifyDate { get; set; }

    [Display(Name = "Full Name", Order = 50)]
    [Description("Fully qualified source view name.")]
    public string FullName => string.Join(".",
        new[] { DatabaseName, SchemaName, ObjectName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
