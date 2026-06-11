namespace SchemaStudioWebViewer.Models
{
    [AttributeUsage(AttributeTargets.Property)]
    public class MultilineDisplayRequiredAttribute : Attribute
    {
        public bool IsMultiline { get; }
        public int InitialHeight { get; }

        public MultilineDisplayRequiredAttribute(bool isMultiline, int height = 100)
        {
            IsMultiline = isMultiline;
            InitialHeight = height;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class DetailViewOnlyAttribute : Attribute
    {
        public bool IsDetailOnly { get; }

        public DetailViewOnlyAttribute(bool isDetailOnly = true)
        {
            IsDetailOnly = isDetailOnly;
        }
    }
}
