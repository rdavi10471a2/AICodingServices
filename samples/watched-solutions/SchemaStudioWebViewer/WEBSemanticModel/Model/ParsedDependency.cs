using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.WEBSemanticModel.Model;

public sealed class ParsedDependency
{
    [Display(Name = "Sequence", Order = 10)]
    [Description("Display order for the parser dependency walk. Direct dependencies of the selected view are listed before their child dependencies.")]
    public int Sequence { get; set; }

    [Display(Name = "Parent", Order = 20)]
    [Description("Fully qualified parent object whose parsed SQL referenced this dependency.")]
    public string ParentName { get; set; } = "";

    [Display(Name = "Kind", Order = 30)]
    [Description("Parser-resolved dependency kind. Views have a parsed child query; tables are terminal named objects.")]
    public string ObjectKind { get; set; } = "";

    [Display(Name = "Depth", Order = 40)]
    [Description("Distance from the selected parsed view. Depth 1 dependencies are referenced directly by the selected view.")]
    public int Depth { get; set; }

    [Display(Name = "Resolved Name", Order = 50)]
    [Description("Fully qualified parser-resolved dependency name.")]
    public string ResolvedName =>
        string.Join(".",
            new[] { Database, Schema, ObjectName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

    [Display(Name = "Database", Order = 60)]
    [Description("Database resolved by the parser walk.")]
    public string Database { get; set; } = "";

    [Display(Name = "Schema", Order = 70)]
    [Description("Schema resolved by the parser walk.")]
    public string Schema { get; set; } = "";

    [Display(Name = "Object", Order = 80)]
    [Description("Object name resolved by the parser walk.")]
    public string ObjectName { get; set; } = "";

    [Display(Name = "Alias", Order = 90)]
    [Description("Alias used at the point where this dependency was encountered.")]
    public string? Alias { get; set; }
}

public static class ParsedDependencyExtensions
{
    public static List<ParsedDependency> GetResolvedDependencies(this ParsedQuery? query)
    {
        var dependencies = new List<ParsedDependency>();
        var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sequence = 0;

        if (query == null)
        {
            return dependencies;
        }

        AddResolvedDependencies(query, dependencies, path, depth: 1, parentName: "Selected view", ref sequence);
        return dependencies;
    }

    private static void AddResolvedDependencies(
        ParsedQuery query,
        List<ParsedDependency> dependencies,
        HashSet<string> path,
        int depth,
        string parentName,
        ref int sequence)
    {
        var namedSources = query.SourceTables
            .Where(source => source.Kind != SourceKind.DerivedQuery && !string.IsNullOrWhiteSpace(source.Table))
            .ToList();

        foreach (var source in namedSources)
        {
            AddNamedDependency(
                source,
                dependencies,
                depth,
                source.NestedQuery != null ? "View" : "Table",
                parentName,
                ref sequence);
        }

        foreach (var source in namedSources.Where(source => source.NestedQuery != null))
        {
            var sourceName = QualifiedName(source.Database, source.Schema, source.Table);
            if (!path.Add(sourceName))
            {
                continue;
            }

            AddResolvedDependencies(
                source.NestedQuery!,
                dependencies,
                path,
                depth + 1,
                sourceName,
                ref sequence);

            path.Remove(sourceName);
        }
    }

    private static void AddNamedDependency(
        SourceTable source,
        List<ParsedDependency> dependencies,
        int depth,
        string objectKind,
        string parentName,
        ref int sequence)
    {
        if (string.IsNullOrWhiteSpace(source.Table))
        {
            return;
        }

        var database = source.Database ?? "";
        var schema = source.Schema ?? "";
        var objectName = source.Table;

        dependencies.Add(new ParsedDependency
        {
            Sequence = ++sequence,
            ParentName = parentName,
            ObjectKind = objectKind,
            Database = database,
            Schema = schema,
            ObjectName = objectName,
            Alias = source.Alias,
            Depth = depth
        });
    }

    private static string QualifiedName(string? database, string? schema, string? objectName) =>
        string.Join(".",
            new[] { database, schema, objectName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
}
