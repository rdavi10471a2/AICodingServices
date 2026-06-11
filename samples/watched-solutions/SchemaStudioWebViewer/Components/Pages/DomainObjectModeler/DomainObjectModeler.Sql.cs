using System.Text;
using System.Text.RegularExpressions;
using Radzen;
using SchemaStudioWebViewer.WEBSemanticModel.Parsing;

[module: SchemaStudio.AIHelpers.AIFileContext(
    "Components/Pages/DomainObjectModeler/DomainObjectModeler.Sql.cs",
    "SQL generation and validation slice for the Domain Object Modeler page.",
    Responsibilities = "Fetch selected base-view SQL, optionally strip internal comments, assemble CREATE OR ALTER VIEW CTE SQL, validate parser compatibility, and quote identifiers.",
    RelatedFiles = "Components/Pages/DomainObjectModeler/DomainObjectModeler.razor; Repositories/ReadOnlyViewDefinitionRepository.cs",
    LastReviewed = "2026-05-13")]
namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private async Task GenerateSqlAsync()
    {
        await SyncTargetInputsAsync();
        EnsureTargetViewNamePrefix();

        if (!CanGenerate)
        {
            StatusMessage = "Choose an anchor and complete every join clause before generating SQL.";
            return;
        }

        IsBusy = true;

        try
        {
            var anchor = AnchorView!;
            var selected = GetAnchorFirstSelectedBaseViews();
            var builder = new StringBuilder();

            builder.AppendLine($"CREATE OR ALTER VIEW {QualifiedName(TargetSchema, TargetViewName)}");
            builder.AppendLine("AS");
            builder.AppendLine("WITH");

            for (var index = 0; index < selected.Count; index++)
            {
                var item = selected[index];
                var sql = await BuildCteBodySqlAsync(item);

                builder.AppendLine($"    {QuoteIdentifier(item.AliasName)} AS");
                builder.AppendLine("    (");
                foreach (var line in sql.SplitLines())
                {
                    builder.AppendLine($"        {line}");
                }

                builder.AppendLine(index == selected.Count - 1 ? "    )" : "    ),");
            }

            builder.AppendLine("SELECT");
            var finalProjectionLines = await BuildFinalProjectionLinesAsync(selected);
            for (var index = 0; index < finalProjectionLines.Count; index++)
            {
                var suffix = index == finalProjectionLines.Count - 1 ? string.Empty : ",";
                builder.AppendLine($"    {finalProjectionLines[index]}{suffix}");
            }

            builder.AppendLine($"FROM {QuoteIdentifier(anchor.AliasName)}");

            foreach (var item in NonAnchorSelectedBaseViews)
            {
                var row = FindJoinRow(item.SchemaObjectId);
                if (row is null || string.IsNullOrWhiteSpace(row.OnClause))
                {
                    continue;
                }

                builder.AppendLine($"{row.JoinType} {QuoteIdentifier(item.AliasName)}");
                AppendOnClause(builder, row.OnClause);
            }

            builder.AppendLine(";");

            GeneratedSql = builder.ToString();
            GeneratedSqlVersion++;
            ShouldHighlightSql = true;
            StatusMessage = StripSourceComments
                ? "Generated SQL with internal source comments removed."
                : "Generated SQL from current anchor, source modes, names, and join rows.";
        }
        catch (Exception ex)
        {
            NotifyFailure("SQL generation failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void AppendOnClause(StringBuilder builder, string onClause)
    {
        var lines = BreakJoinConjunctions(onClause)
            .SplitLines()
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        builder.AppendLine($"    ON {lines[0]}");
        foreach (var line in lines.Skip(1))
        {
            builder.AppendLine($"       {line}");
        }
    }

    private static string BreakJoinConjunctions(string onClause) =>
        Regex.Replace(onClause.Trim(), @"\s+\b(AND|OR)\b\s+", match =>
            $"{Environment.NewLine}{match.Groups[1].Value.ToUpperInvariant()} ", RegexOptions.IgnoreCase);

    private List<DomainBaseViewItem> GetAnchorFirstSelectedBaseViews()
    {
        var selected = SelectedBaseViews.ToList();
        var anchor = AnchorView;
        if (anchor is null)
        {
            return selected;
        }

        return selected
            .OrderByDescending(item => ReferenceEquals(item, anchor))
            .ThenBy(item => item.SelectionOrdinal)
            .ToList();
    }

    private async Task<string> BuildCteBodySqlAsync(DomainBaseViewItem item)
    {
        if (item.CteSourceMode == CteSourceModeViewSurface)
        {
            return await BuildViewSurfaceSqlAsync(item);
        }

        var definition = await ReadOnlyViewDefinitionRepository.GetViewDefinitionAsync(
            item.Source.SourceDatabaseName ?? SelectedDatabaseName(),
            item.Source.SourceSchemaName,
            item.Source.SourceObjectName);

        var sql = definition?.Definition
            ?? $"SELECT * FROM {QualifiedName(item.Source.SourceDatabaseName ?? SelectedDatabaseName(), item.Source.SourceSchemaName, item.Source.SourceObjectName)}";
        if (StripSourceComments)
        {
            sql = ReadOnlyViewDefinitionRepository.CleanSqlDefinition(sql);
        }

        return ExtractSelectableSql(sql);
    }

    private async Task<string> BuildViewSurfaceSqlAsync(DomainBaseViewItem item)
    {
        var columns = (await SchemaObjectColumnRepository.GetByObjectAsync(item.SchemaObjectId))
            .Where(column => !string.IsNullOrWhiteSpace(column.SourceColumnName))
            .OrderBy(column => column.OrdinalPosition)
            .ToList();

        var sourceAlias = SanitizeAlias(item.AliasName);
        var sourceName = QualifiedName(
            item.Source.SourceDatabaseName ?? SelectedDatabaseName(),
            item.Source.SourceSchemaName,
            item.Source.SourceObjectName);

        if (columns.Count == 0)
        {
            return $"SELECT *{Environment.NewLine}FROM {sourceName} AS {QuoteIdentifier(sourceAlias)}";
        }

        var builder = new StringBuilder();
        builder.AppendLine("SELECT");

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index].SourceColumnName;
            var prefix = index == 0 ? "    " : "    , ";
            builder.Append(prefix);
            builder.Append(QuoteIdentifier(sourceAlias));
            builder.Append('.');
            builder.Append(QuoteIdentifier(column));
            builder.Append(" AS ");
            builder.Append(QuoteIdentifier(column));
            builder.AppendLine();
        }

        builder.Append("FROM ");
        builder.Append(sourceName);
        builder.Append(" AS ");
        builder.Append(QuoteIdentifier(sourceAlias));
        return builder.ToString();
    }

    private async Task<IReadOnlyList<string>> BuildFinalProjectionLinesAsync(IReadOnlyList<DomainBaseViewItem> selected)
    {
        var projectionLines = new List<string>();

        foreach (var item in selected)
        {
            var columns = await SchemaObjectColumnRepository.GetByObjectAsync(item.SchemaObjectId);
            foreach (var column in columns.Where(column => !string.IsNullOrWhiteSpace(column.SourceColumnName)))
            {
                projectionLines.Add(
                    $"{QuoteIdentifier(item.AliasName)}.{QuoteIdentifier(column.SourceColumnName)} AS {QuoteIdentifier(BuildOutputColumnName(item.AliasName, column.SourceColumnName))}");
            }
        }

        return projectionLines.Count > 0
            ? projectionLines
            : selected.Select(item => $"{QuoteIdentifier(item.AliasName)}.*").ToList();
    }

    private void ValidateSql()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql))
        {
            StatusMessage = "Generate SQL before validating.";
            return;
        }

        try
        {
            var parser = new ViewParser();
            var result = parser.Parse(GeneratedSql);
            StatusMessage = result is not null
                ? "Parser validation completed."
                : "Parser validation returned no result.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Parser validation failed: {ex.Message}";
            NotificationService.Notify(NotificationSeverity.Error, "Validation failed", ex.Message, 6000);
        }
    }

    private string SelectedDatabaseName() =>
        Databases.FirstOrDefault(database => database.DatabaseId == SelectedDatabaseId)?.DatabaseName ?? string.Empty;

    private static string ExtractSelectableSql(string sql)
    {
        var normalized = sql.Trim().TrimEnd(';').Trim();
        var match = Regex.Match(
            normalized,
            @"\bAS\b\s*(?<body>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success
            ? match.Groups["body"].Value.Trim()
            : normalized;
    }

    private static string QualifiedName(params string?[] parts) =>
        string.Join(".", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => QuoteIdentifier(part!)));

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string BuildOutputColumnName(string aliasName, string sourceColumnName) =>
        $"{aliasName}_{sourceColumnName}";
}

internal static class DomainObjectModelerStringExtensions
{
    public static IEnumerable<string> SplitLines(this string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}

