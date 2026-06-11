using SchemaStudio.AIHelpers;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.Models
{
    [FileVersion("1.0")]
    [AIFileContext("Models/DatabaseModel.cs", "Defines the web-facing database metadata model used by read-only pages and UI-bound forms.", LastReviewed = "2026-04-23")]
    [AIChange("1.0", "2026-04-23 12:58 PM CDT added ViewNameFilter so the web model can carry per-database view-name filtering metadata.", AICommandStatus.Pending)]
    // 2026-04-23 12:58 PM CDT AI v1.0 database-web-filter marker: web database metadata now carries the view-name filter field for UI selection rules.
    public class DatabaseModel
    {
        [Display(AutoGenerateField = false)]
        public int DatabaseId { get; set; }

        [Display(Name = "Database Name", Order = 10)]
        [Description("The physical SQL Server database name.")]
        public string DatabaseName { get; set; } = "";

        [Display(AutoGenerateField = false)]
        [Description("Default Schema for the Database")]
        public string DefaultSchema { get; set; } = "";

        [Display(Name = "Business Alias", Order = 30)]
        [Description("Common Name for the Database in the Enterprise.")]
        public string? BusinessName { get; set; }

        [Display(Name = "Summary", Order = 40)]
        [Description("A high-level overview of what this data source represents within the Enterprise.")]
        [MultilineDisplayRequired(true, 150)]
        public string? BusinessDescription { get; set; }

        [Display(Name = "Technical Notes", Order = 50)]
        [Description("Additional Notes")]
        [MultilineDisplayRequired(true, 250)]
        public string? DeveloperNotes { get; set; }

        [Display(Name = "View Name Filter", Order = 60)]
        [Description("Optional pattern or expression used to include only matching source view names for this database.")]
        public string? ViewNameFilter { get; set; }

        [Display(AutoGenerateField = false)]
        public bool Active { get; set; }
    }
}
