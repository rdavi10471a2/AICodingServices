using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

public sealed class SqlObjectDependency
{
    [Display(Name = "Direction", Order = 10)]
    [Description("Dependency direction returned by SQL Server metadata.")]
    public string Direction { get; set; } = "";

    [Display(Name = "Database", Order = 20)]
    [Description("Database containing the dependent or referenced object.")]
    public string? DatabaseName { get; set; }

    [Display(Name = "Schema", Order = 30)]
    [Description("Schema containing the dependent or referenced object.")]
    public string? SchemaName { get; set; }

    [Display(Name = "Object", Order = 40)]
    [Description("Dependent or referenced object name.")]
    public string? ObjectName { get; set; }

    [Display(Name = "Full Name", Order = 50)]
    [Description("Resolved dependency name assembled for display.")]
    public string FullName => string.Join(".", new[] { DatabaseName, SchemaName, ObjectName }
        .Where(part => !string.IsNullOrWhiteSpace(part)));
}
