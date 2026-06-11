using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

public sealed class DatabaseDomainDefinition
{
    [Display(AutoGenerateField = false)]
    public int DatabaseDomainId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    [Display(AutoGenerateField = false)]
    public int DatabaseId { get; set; }

    [Required]
    [StringLength(128)]
    [Display(Name = "Domain", Order = 10)]
    [Description("Functional area for this database, such as General Ledger, AP, Payroll, or Service.")]
    public string Domain { get; set; } = "";

    [StringLength(500)]
    [Display(Name = "Description", Order = 20)]
    [Description("Optional description for this database domain.")]
    public string? Description { get; set; }
}
