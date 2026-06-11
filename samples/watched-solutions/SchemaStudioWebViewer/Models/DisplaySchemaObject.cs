using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.Models
{
    [Display(AutoGenerateField = false)]
    public class DisplaySchemaObject
    {
        public int? SchemaObjectId { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsHeader { get; set; }
        public bool IsBaseObject { get; set; }
    }
}
