using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.Models
{
    public class DatabaseDomainModel
    {
        [Display(AutoGenerateField = false)]
        public int DatabaseDomainId { get; set; }

        [Display(AutoGenerateField = false)]
        public int DatabaseId { get; set; }

        [Display(Name = "Business Domain", Order = 10)]
        [Description("The functional area (e.g., General Ledger, AP, Payroll).")]
        public string Domain { get; set; } = "";
    }
}
