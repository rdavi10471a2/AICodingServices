using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.Models
{
    public class SchemaObjectColumnModel
    {
        [Display(AutoGenerateField = false)]
        public int SchemaObjectColumnId { get; set; }

        [Display(AutoGenerateField = false)]
        public int SchemaObjectId { get; set; }

        [Display(
            Name = "Base Database",
            Description = "Underlying source database for the base schema object.",
            AutoGenerateField = false
        )]
        public string BaseDatabaseName { get; set; } = "";

        [Display(
            Name = "Base Schema",
            Description = "Underlying source schema for the base schema object.",
            AutoGenerateField = false
        )]
        public string BaseSchemaName { get; set; } = "";

        [Display(
            Name = "Base Object",
            Description = "Underlying source table/object for this view column.",
            AutoGenerateField = false
        )]
        public string BaseObjectName { get; set; } = "";

        [Display(
            Name = "Base Column",
            Description = "Underlying source Column.",
            AutoGenerateField = false
        )]
        public string BaseColumnName { get; set; } = "";

        [Display(
            Name = "Fully Qualified Source",
            Description = "Fully qualified Phyisical Source for this column.",
            AutoGenerateField = true, Order = 11
        )]
        [DetailViewOnly]
        public string FullyQualifiedSourceColumnName =>
            SqlQualify(
                BaseDatabaseName,
                BaseSchemaName,
                BaseObjectName,
                BaseColumnName
            );

        [Display(Name = "Sequence", Order = 10)]
        [Description("The ordinal position of the field within the view definition.")]
        public int OrdinalPosition { get; set; }

        [Display(Name = "View Column Name", Order = 20)]
        [Description("The Column Name used in the view.")]
        public string SourceColumnName { get; set; } = "";

        [Display(AutoGenerateField = false)]
        public string? SourceColumnKind { get; set; }

        [Display(Name = "Business Name", Order = 30)]
        [Description("Business Name for this column.")]
        public string? BusinessName { get; set; }

        [Display(Name = "Description", Order = 40)]
        [Description("Overview of the purpose of this column and what it represents")]
        [MultilineDisplayRequired(true, 150)]
        public string? BusinessDescription { get; set; }

        [Display(Name = "Dev Notes", Order = 50)]
        [Description("Additional Notes and information about this column and details on advanced use")]
        [MultilineDisplayRequired(true, 450)]
        public string? DeveloperNotes { get; set; }

        [Display(AutoGenerateField = false)]
        public bool IsBaseDefinition { get; set; }

        [Display(AutoGenerateField = false)]
        public bool? DisableInheritance { get; set; }

        [Display(AutoGenerateField = false)]
        public DateTime LastSynced { get; set; }

        public static string SqlQualify(params string[] parts) =>
            string.Join(".",
                parts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => $"[{x.Replace("]", "]]", StringComparison.Ordinal)}]"));
    }
}
