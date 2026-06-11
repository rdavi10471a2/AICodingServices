namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    public enum SourceKind
    {
        NamedObject,
        Cte,
        DerivedQuery,
        Function
    }

    public enum ColumnKind
    {
        Simple,
        Aggregate,
        Window,
        Expression
    }
}

