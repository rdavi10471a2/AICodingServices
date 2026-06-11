using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectEditor;

[FileVersion("1.7")]
[AIFileContext(
    "Services/CteSelectionPresentationEditor.cs",
    "Presentation helpers for the domain object editor candidate page.",
    Responsibilities = "Translate selection and SQL line state into CSS classes and display labels used by Razor markup.",
    Nuances = "Keep this small; the real selection rules belong in CteSelectionSession while the Razor page owns actual controls.")]
public static class CteSelectionPresentationEditor
{
    public static string CteTabText(TrimmedCteDefinition cte)
    {
        return cte.IsKept ? cte.Name : $"{cte.Name} (removed)";
    }

    public static string LineStateClass(TrimmedSqlLineState state)
    {
        return state switch
        {
            TrimmedSqlLineState.Selected => "cte-sql-line--selected",
            TrimmedSqlLineState.Required
                or TrimmedSqlLineState.RequiredByWhere
                or TrimmedSqlLineState.RequiredByJoinAndWhere => "cte-sql-line--required",
            _ => "cte-sql-line--removed"
        };
    }

    public static string LineStateClass(QueryRowStateEditor state)
    {
        return LineStateClass(state.DisplayState);
    }

    public static string PathLineStateClass(QueryRowStateEditor state)
    {
        return state.IsActive ? "cte-sql-line--selected" : "cte-sql-line--removed";
    }

    public static string LineStateClass(TrimmedSqlLine line)
    {
        if (line.State == TrimmedSqlLineState.Required && IsSqlStructureLine(line.Text))
        {
            return "cte-sql-line--selected";
        }

        return LineStateClass(line.State);
    }

    private static bool IsSqlStructureLine(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Equals("(", StringComparison.Ordinal)
            || trimmed.Equals(")", StringComparison.Ordinal)
            || trimmed.Equals("WITH", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith(" AS", StringComparison.OrdinalIgnoreCase);
    }

}

