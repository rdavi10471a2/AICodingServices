namespace SchemaStudioWebViewer.Models
{
    public class HelpSubject
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Icon { get; set; } = "help";
        public List<HelpDetail> Details { get; set; } = new();

        public HelpSubject() { }

        public HelpSubject(string id, string title, string subtitle, string icon, List<HelpDetail> details = null)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            Icon = icon;
            Details = details ?? new List<HelpDetail>();
        }
    }

    public class HelpDetail
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = "help";
        public string Subtitle { get; set; } = string.Empty;
        public string CustomFragmentName { get; set; } = string.Empty;

        public HelpDetail() { }
    }
}
