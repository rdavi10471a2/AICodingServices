using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.Models
{
    public class SchemaObjectModel
    {
        [Display(AutoGenerateField = false)]
        public int SchemaObjectId { get; set; }

        [Display(AutoGenerateField = false)]
        public int DatabaseId { get; set; }

        [Display(AutoGenerateField = false)]
        public string? SourceDatabaseName { get; set; }

        [Display(AutoGenerateField = false)]
        public string SourceSchemaName { get; set; } = "";

        [Display(AutoGenerateField = false)]
        public string SourceObjectName { get; set; } = "";

        [Display(Name = "Physical Source", Order = 5)]
        [Description("The fully qualified 3-part name of the source object.")]
        [DetailViewOnly]
        public string SourceName => string.Join(
            ".",
            new[] { SourceDatabaseName, SourceSchemaName, SourceObjectName }
                .Select(x => string.IsNullOrWhiteSpace(x) ? "[unknown]" : x));

        [Display(Name = "Business Name", Order = 30)]
        [Description("Name for this View Object that represents is Business Meaning.")]
        public string? BusinessName { get; set; }

        [Display(Name = "Description", Order = 40)]
        [Description("Overview of the purpose of this view and what it represents")]
        [MultilineDisplayRequired(true, 150)]
        public string? BusinessDescription { get; set; }

        [Display(Name = "Dev Notes", Order = 50)]
        [Description("Additional Notes and information about this view and details on advanced use")]
        [MultilineDisplayRequired(true, 250)]
        public string? DeveloperNotes { get; set; }

        [Display(Name = "Composition Definition", Order = 55)]
        [Description("JSON recipe used to compose this derived domain object from managed base views.")]
        [MultilineDisplayRequired(true, 250)]
        public string? CompositionDefinitionJson { get; set; }

        [Display(Name = "Base Object", Order = 60)]
        [Description("Indicates if this view represents a root business concept")]
        public bool IsBaseObject { get; set; }

        [Display(Name = "Domain", Order = 70)]
        [Description("The functional area (e.g., General Ledger, AP, Payroll) of the business that the data for this view belongs to")]
        public string? Domain { get; set; }

        [Display(AutoGenerateField = false)]
        public bool IsActive { get; set; }

        [Display(AutoGenerateField = false)]
        public DateTime LastSynced { get; set; }
    }
}
