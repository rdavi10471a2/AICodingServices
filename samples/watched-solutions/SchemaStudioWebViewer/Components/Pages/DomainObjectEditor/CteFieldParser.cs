using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaStudio.AIHelpers;
using System.Text;

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectEditor;

[FileVersion("2.20")]
[AIFileContext(
    "Services/CteFieldParser.cs",
    "Parses visible SQL CTE definitions into a selectable object model for local construction-by-subtraction trimming.",
    Responsibilities = "Use ScriptDom to capture the current CTE surface: fields, sources, joins, final select fields, and the visible alias dependencies needed to keep the trimmed SQL valid.",
    Nuances = "This is a local validity-preserving object shaper, not a physical lineage engine. A view is treated as an already-valid table-shaped source unless its definition is inlined into the CTE being trimmed.")]
public sealed class CteFieldParser
{
    public CteParseResult Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return CteParseResult.Failed("Paste a SQL statement with CTEs in the input box.");
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
        {
            return CteParseResult.Failed(string.Join(
                Environment.NewLine,
                errors.Select(error => $"Parse error line {error.Line}, column {error.Column}: {error.Message}")));
        }

        var builder = new CteModelBuilder(fragment.ScriptTokenStream);
        fragment.Accept(builder);
        return builder.Result;
    }

    public CteValidationResult ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new CteValidationResult(false, "No SQL to validate.");
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        parser.Parse(reader, out var errors);
        if (errors.Count == 0)
        {
            return new CteValidationResult(true, "ScriptDom validation passed.");
        }

        return new CteValidationResult(
            false,
            string.Join(
                Environment.NewLine,
                errors.Select(error => $"Parse error line {error.Line}, column {error.Column}: {error.Message}")));
    }

    public string GenerateSimpleCteSql(string sql)
    {
        var result = Parse(sql);
        if (!result.IsSuccess)
        {
            return result.ErrorMessage;
        }

        if (result.Ctes.Count > 0 || result.FinalQuery.Fields.Count == 0)
        {
            return sql.Trim();
        }

        return $"-- User input modified to create a legal CTE for processing.{Environment.NewLine}"
            + BuildSimpleCteSql(result);
    }

    public string Describe(CteParseResult result)
    {
        if (!result.IsSuccess)
        {
            return result.ErrorMessage;
        }

        var sb = new StringBuilder();
        foreach (var cte in result.Ctes)
        {
            sb.AppendLine(cte.Name);
            foreach (var field in cte.Fields)
            {
                sb.AppendLine($"  {field.Name}");
            }

            if (cte.Sources.Count > 0)
            {
                sb.AppendLine("  sources");
                foreach (var source in cte.Sources)
                {
                    sb.AppendLine($"    {source.DisplayText}");
                }
            }

            if (cte.Joins.Count > 0)
            {
                sb.AppendLine("  joins");
                foreach (var join in cte.Joins)
                {
                    sb.AppendLine($"    {join.DisplayText}");
                }
            }
        }

        if (result.FinalQuery.Fields.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine("Final Select");
            foreach (var field in result.FinalQuery.Fields)
            {
                sb.AppendLine($"  {field.Name}");
            }

            if (result.FinalQuery.Sources.Count > 0)
            {
                sb.AppendLine("  sources");
                foreach (var source in result.FinalQuery.Sources)
                {
                    sb.AppendLine($"    {source.DisplayText}");
                }
            }

            if (result.FinalQuery.Joins.Count > 0)
            {
                sb.AppendLine("  joins");
                foreach (var join in result.FinalQuery.Joins)
                {
                    sb.AppendLine($"    {join.DisplayText}");
                }
            }
        }

        AppendStatementTerminator(sb);
        return sb.ToString().TrimEnd();
    }

    public string GenerateTrimmedSql(CteParseResult result, IReadOnlySet<string> selectedFieldKeys)
    {
        if (!result.IsSuccess)
        {
            return result.ErrorMessage;
        }

        var requiredFieldKeys = ExpandFinalJoinFieldKeys(result, selectedFieldKeys);
        var keptCtes = result.Ctes
            .Select(cte => new
            {
                Cte = cte,
                SelectedFields = cte.Fields
                    .Where(field => requiredFieldKeys.Contains(field.Key))
                    .ToList()
            })
            .Where(item => item.SelectedFields.Count > 0)
            .ToList();

        if (keptCtes.Count == 0)
        {
            return "-- Select at least one object field.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("WITH");
        for (var index = 0; index < keptCtes.Count; index++)
        {
            var item = keptCtes[index];
            var requiredAliases = ResolveRequiredAliases(item.Cte, item.SelectedFields);
            var keptSources = item.Cte.Sources
                .Where(source => requiredAliases.Contains(source.Alias))
                .ToList();
            var keptJoins = item.Cte.Joins
                .Where(join => requiredAliases.Contains(join.RightAlias))
                .ToList();

            sb.AppendLine($"{item.Cte.Name} AS");
            sb.AppendLine("(");
            sb.AppendLine("    SELECT");
            for (var fieldIndex = 0; fieldIndex < item.SelectedFields.Count; fieldIndex++)
            {
                var field = item.SelectedFields[fieldIndex];
                var comma = fieldIndex == 0 ? "       " : "     , ";
                sb.AppendLine($"{comma}{field.ToSelectSql()}");
            }

            var root = keptSources.FirstOrDefault() ?? item.Cte.Sources.FirstOrDefault();
            if (root is not null)
            {
                sb.AppendLine($"    FROM {root.DisplayText}");
            }

            foreach (var join in keptJoins)
            {
                sb.AppendLine($"    {join.DisplayText}");
            }

            AppendWhereClause(sb, item.Cte.WhereClauseText, "    ");

            sb.Append(index == keptCtes.Count - 1 ? ")" : "),");
            sb.AppendLine();
        }

        var finalFields = BuildTrimmedFinalFields(result, selectedFieldKeys);
        sb.AppendLine();
        sb.AppendLine("SELECT");
        for (var index = 0; index < finalFields.Count; index++)
        {
            var comma = index == 0 ? "   " : " , ";
            sb.AppendLine($"{comma}{finalFields[index]}");
        }

        var finalSources = result.FinalQuery.Sources
            .Where(source => keptCtes.Any(item => string.Equals(item.Cte.Name, source.ObjectName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var finalAliases = finalSources
            .Select(source => source.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalRoot = finalSources.FirstOrDefault();
        if (finalRoot is not null)
        {
            sb.AppendLine($"FROM {finalRoot.DisplayText}");
        }

        foreach (var join in result.FinalQuery.Joins.Where(join => finalAliases.Contains(join.RightAlias)))
        {
            sb.AppendLine(join.DisplayText);
        }

        AppendStatementTerminator(sb);
        return sb.ToString().TrimEnd();
    }

    public string GenerateTrimmedDerivedTableSql(CteParseResult result, IReadOnlySet<string> selectedFieldKeys)
    {
        if (!result.IsSuccess)
        {
            return result.ErrorMessage;
        }

        var requiredFieldKeys = ExpandFinalJoinFieldKeys(result, selectedFieldKeys);
        var keptCtes = result.Ctes
            .Select(cte => new
            {
                Cte = cte,
                SelectedFields = cte.Fields
                    .Where(field => requiredFieldKeys.Contains(field.Key))
                    .ToList()
            })
            .Where(item => item.SelectedFields.Count > 0)
            .ToList();

        if (keptCtes.Count == 0)
        {
            return "-- Select at least one object field.";
        }

        if (keptCtes.Count == 1)
        {
            var only = keptCtes[0];
            return BuildTrimmedSelectSql(only.Cte, only.SelectedFields);
        }

        var keptByName = keptCtes.ToDictionary(item => item.Cte.Name, StringComparer.OrdinalIgnoreCase);
        var finalSources = result.FinalQuery.Sources
            .Where(source => keptByName.ContainsKey(source.ObjectName))
            .ToList();
        var finalAliases = finalSources
            .Select(source => source.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalRoot = finalSources.FirstOrDefault();

        var sb = new StringBuilder();
        var finalFields = BuildTrimmedFinalFields(result, selectedFieldKeys);
        sb.AppendLine("SELECT");
        for (var index = 0; index < finalFields.Count; index++)
        {
            var comma = index == 0 ? "   " : " , ";
            sb.AppendLine($"{comma}{finalFields[index]}");
        }

        if (finalRoot is not null && keptByName.TryGetValue(finalRoot.ObjectName, out var root))
        {
            sb.AppendLine("FROM");
            AppendDerivedObject(sb, root.Cte, root.SelectedFields, finalRoot.Alias, string.Empty);
        }

        foreach (var join in result.FinalQuery.Joins.Where(join => finalAliases.Contains(join.RightAlias)))
        {
            var source = finalSources.FirstOrDefault(candidate =>
                string.Equals(candidate.Alias, join.RightAlias, StringComparison.OrdinalIgnoreCase));
            if (source is null || !keptByName.TryGetValue(source.ObjectName, out var joined))
            {
                continue;
            }

            sb.AppendLine($"{BuildDerivedJoinPrefix(join)}");
            AppendDerivedObject(sb, joined.Cte, joined.SelectedFields, source.Alias, string.Empty);
            if (!string.IsNullOrWhiteSpace(join.ConditionText))
            {
                sb.AppendLine($"ON {join.ConditionText}");
            }
        }

        AppendStatementTerminator(sb);
        return sb.ToString().TrimEnd();
    }

    public IReadOnlyList<TrimmedCteDefinition> GenerateTrimmedCtes(
        CteParseResult result,
        IReadOnlySet<string> selectedFieldKeys)
    {
        return GenerateTrimmedCtes(result, selectedFieldKeys, selectedFieldKeys);
    }

    public IReadOnlyList<TrimmedCteDefinition> GenerateTrimmedCtes(
        CteParseResult result,
        IReadOnlySet<string> selectedFieldKeys,
        IReadOnlySet<string> requiredOnlyFieldKeys)
    {
        if (!result.IsSuccess)
        {
            return Array.Empty<TrimmedCteDefinition>();
        }

        var activeFieldKeys = selectedFieldKeys
            .Concat(requiredOnlyFieldKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return result.Ctes
            .Select(cte =>
            {
                var selectedFields = cte.Fields
                    .Where(field => activeFieldKeys.Contains(field.Key))
                    .ToList();

                return selectedFields.Count == 0
                    ? new TrimmedCteDefinition(
                        cte.Name,
                        string.Empty,
                        false,
                        0,
                        0,
                        cte.Fields.Count,
                        BuildTrimmedCteLines(cte, selectedFieldKeys, requiredOnlyFieldKeys))
                    : new TrimmedCteDefinition(
                        cte.Name,
                        BuildTrimmedCteSql(cte, selectedFields),
                        true,
                        selectedFields.Count(field => selectedFieldKeys.Contains(field.Key)),
                        selectedFields.Count,
                        cte.Fields.Count,
                        BuildTrimmedCteLines(cte, selectedFieldKeys, requiredOnlyFieldKeys));
            })
            .ToList();
    }

    public IReadOnlySet<string> GetRequiredFieldKeys(CteParseResult result, IReadOnlySet<string> selectedFieldKeys)
    {
        return result.IsSuccess
            ? ExpandFinalJoinFieldKeys(result, selectedFieldKeys)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<TrimmedSqlLine> BuildTrimmedCteLines(
        CteDefinition cte,
        IReadOnlySet<string> selectedFieldKeys,
        IReadOnlySet<string> requiredOnlyFieldKeys)
    {
        var activeKeys = selectedFieldKeys
            .Concat(requiredOnlyFieldKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeFields = cte.Fields
            .Where(field => activeKeys.Contains(field.Key))
            .ToList();
        var isKept = activeFields.Count > 0;
        var requiredAliases = isKept
            ? ResolveRequiredAliases(cte, activeFields)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var joinRequiredAliases = isKept
            ? ResolveJoinRequiredAliases(cte, activeFields)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var whereRequiredAliases = isKept
            ? ResolveWhereRequiredAliases(cte)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptJoinAliases = cte.Joins
            .Where(join => requiredAliases.Contains(join.RightAlias))
            .Select(join => join.RightAlias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = isKept
            ? cte.Sources.FirstOrDefault(source => requiredAliases.Contains(source.Alias)) ?? cte.Sources.FirstOrDefault()
            : null;
        var lines = new List<TrimmedSqlLine>
        {
            new("WITH", isKept ? TrimmedSqlLineState.Required : TrimmedSqlLineState.Removed),
            new($"{cte.Name} AS", isKept ? TrimmedSqlLineState.Required : TrimmedSqlLineState.Removed),
            new("(", isKept ? TrimmedSqlLineState.Required : TrimmedSqlLineState.Removed),
            new("    SELECT", isKept ? TrimmedSqlLineState.Required : TrimmedSqlLineState.Removed)
        };

        for (var index = 0; index < cte.Fields.Count; index++)
        {
            var comma = index == 0 ? "       " : "     , ";
            var field = cte.Fields[index];
            var state = selectedFieldKeys.Contains(field.Key)
                ? TrimmedSqlLineState.Selected
                : requiredOnlyFieldKeys.Contains(field.Key)
                    ? TrimmedSqlLineState.Required
                    : TrimmedSqlLineState.Removed;
            lines.Add(new TrimmedSqlLine($"{comma}{field.ToSelectSql()}", state));
        }

        var displayRoot = root ?? cte.Sources.FirstOrDefault();
        if (displayRoot is not null)
        {
            lines.Add(new TrimmedSqlLine(
                $"    FROM {displayRoot.DisplayText}",
                root is not null ? TrimmedSqlLineState.Required : TrimmedSqlLineState.Removed));
        }

        foreach (var join in cte.Joins)
        {
            var isRequiredByJoin = joinRequiredAliases.Contains(join.RightAlias);
            var isRequiredByWhere = whereRequiredAliases.Contains(join.RightAlias);
            var state = keptJoinAliases.Contains(join.RightAlias)
                ? isRequiredByJoin && isRequiredByWhere
                    ? TrimmedSqlLineState.RequiredByJoinAndWhere
                    : isRequiredByWhere
                        ? TrimmedSqlLineState.RequiredByWhere
                        : TrimmedSqlLineState.Required
                : TrimmedSqlLineState.Removed;
            lines.Add(new TrimmedSqlLine(
                $"    {join.DisplayText}",
                state));
        }

        if (!string.IsNullOrWhiteSpace(cte.WhereClauseText))
        {
            lines.Add(new TrimmedSqlLine($"    WHERE {cte.WhereClauseText}", isKept ? TrimmedSqlLineState.RequiredByWhere : TrimmedSqlLineState.Removed));
        }

        lines.Add(new TrimmedSqlLine(")", isKept ? TrimmedSqlLineState.Required : TrimmedSqlLineState.Removed));
        return lines;
    }

    private static string BuildSimpleCteSql(CteParseResult result)
    {
        const string objectName = "SourceObject";
        const string alias = "source";
        var sb = new StringBuilder();
        sb.AppendLine("WITH");
        sb.AppendLine($"{objectName} AS");
        sb.AppendLine("(");
        sb.AppendLine("    SELECT");
        for (var index = 0; index < result.FinalQuery.Fields.Count; index++)
        {
            var field = result.FinalQuery.Fields[index];
            var comma = index == 0 ? "       " : "     , ";
            sb.AppendLine($"{comma}{field.ToSelectSql()}");
        }

        if (result.FinalQuery.Sources.FirstOrDefault() is { } root)
        {
            sb.AppendLine($"    FROM {root.DisplayText}");
        }

        foreach (var join in result.FinalQuery.Joins)
        {
            sb.AppendLine($"    {join.DisplayText}");
        }

        AppendWhereClause(sb, result.FinalQuery.WhereClauseText, "    ");

        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("SELECT");
        for (var index = 0; index < result.FinalQuery.Fields.Count; index++)
        {
            var field = result.FinalQuery.Fields[index];
            var comma = index == 0 ? "   " : " , ";
            sb.AppendLine($"{comma}{alias}.{field.Name}");
        }

        sb.AppendLine($"FROM {objectName} AS {alias}");
        AppendStatementTerminator(sb);
        return sb.ToString().TrimEnd();
    }

    private static HashSet<string> ExpandFinalJoinFieldKeys(
        CteParseResult result,
        IReadOnlySet<string> selectedFieldKeys)
    {
        var required = new HashSet<string>(selectedFieldKeys, StringComparer.OrdinalIgnoreCase);
        var finalSources = result.FinalQuery.Sources
            .Where(source => result.Ctes.Any(cte => string.Equals(cte.Name, source.ObjectName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var finalAliases = finalSources
            .Where(source => selectedFieldKeys.Any(key => key.StartsWith(source.ObjectName + ".", StringComparison.OrdinalIgnoreCase)))
            .Select(source => source.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var join in result.FinalQuery.Joins)
            {
                if (!IsActiveFinalJoin(join, finalAliases))
                {
                    continue;
                }

                foreach (var alias in GetJoinAliases(join))
                {
                    if (finalAliases.Add(alias))
                    {
                        changed = true;
                    }
                }
            }
        }

        var ctesByName = result.Ctes.ToDictionary(cte => cte.Name, StringComparer.OrdinalIgnoreCase);
        var activeFinalJoins = result.FinalQuery.Joins
            .Where(join => IsActiveFinalJoin(join, finalAliases))
            .ToList();
        foreach (var reference in activeFinalJoins.SelectMany(join => join.ColumnRefs))
        {
            var source = finalSources.FirstOrDefault(candidate =>
                string.Equals(candidate.Alias, reference.Alias, StringComparison.OrdinalIgnoreCase));
            if (source is null
                || !finalAliases.Contains(source.Alias)
                || !ctesByName.TryGetValue(source.ObjectName, out var cte))
            {
                continue;
            }

            var field = cte.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, reference.Column, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                required.Add(field.Key);
            }
        }

        return required;
    }

    private static bool IsActiveFinalJoin(JoinRef join, IReadOnlySet<string> activeAliases)
    {
        if (activeAliases.Contains(join.RightAlias))
        {
            return true;
        }

        return join.ColumnRefs
            .Select(reference => reference.Alias)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(activeAliases.Contains) > 1;
    }

    private static IEnumerable<string> GetJoinAliases(JoinRef join)
    {
        yield return join.RightAlias;
        foreach (var alias in join.ColumnRefs.Select(reference => reference.Alias))
        {
            yield return alias;
        }
    }

    private static string BuildTrimmedCteSql(CteDefinition cte, IReadOnlyList<CteField> selectedFields)
    {
        var requiredAliases = ResolveRequiredAliases(cte, selectedFields);
        var keptSources = cte.Sources
            .Where(source => requiredAliases.Contains(source.Alias))
            .ToList();
        var keptJoins = cte.Joins
            .Where(join => requiredAliases.Contains(join.RightAlias))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"{cte.Name} AS");
        sb.AppendLine("(");
        sb.AppendLine("    SELECT");
        for (var index = 0; index < selectedFields.Count; index++)
        {
            var comma = index == 0 ? "       " : "     , ";
            sb.AppendLine($"{comma}{selectedFields[index].ToSelectSql()}");
        }

        var root = keptSources.FirstOrDefault() ?? cte.Sources.FirstOrDefault();
        if (root is not null)
        {
            sb.AppendLine($"    FROM {root.DisplayText}");
        }

        foreach (var join in keptJoins)
        {
            sb.AppendLine($"    {join.DisplayText}");
        }

        AppendWhereClause(sb, cte.WhereClauseText, "    ");

        sb.Append(")");
        return sb.ToString();
    }

    private static string BuildTrimmedSelectSql(CteDefinition cte, IReadOnlyList<CteField> selectedFields)
    {
        var requiredAliases = ResolveRequiredAliases(cte, selectedFields);
        var keptSources = cte.Sources
            .Where(source => requiredAliases.Contains(source.Alias))
            .ToList();
        var keptJoins = cte.Joins
            .Where(join => requiredAliases.Contains(join.RightAlias))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("SELECT");
        for (var index = 0; index < selectedFields.Count; index++)
        {
            var comma = index == 0 ? "   " : " , ";
            sb.AppendLine($"{comma}{selectedFields[index].ToSelectSql()}");
        }

        var root = keptSources.FirstOrDefault() ?? cte.Sources.FirstOrDefault();
        if (root is not null)
        {
            sb.AppendLine($"FROM {root.DisplayText}");
        }

        foreach (var join in keptJoins)
        {
            sb.AppendLine(join.DisplayText);
        }

        AppendWhereClause(sb, cte.WhereClauseText, string.Empty);

        return sb.ToString().TrimEnd();
    }

    private static void AppendDerivedObject(
        StringBuilder sb,
        CteDefinition cte,
        IReadOnlyList<CteField> selectedFields,
        string alias,
        string indent)
    {
        var requiredAliases = ResolveRequiredAliases(cte, selectedFields);
        var keptSources = cte.Sources
            .Where(source => requiredAliases.Contains(source.Alias))
            .ToList();
        var keptJoins = cte.Joins
            .Where(join => requiredAliases.Contains(join.RightAlias))
            .ToList();

        sb.AppendLine($"{indent}(");
        sb.AppendLine($"{indent}    SELECT");
        for (var index = 0; index < selectedFields.Count; index++)
        {
            var comma = index == 0 ? "       " : "     , ";
            sb.AppendLine($"{indent}    {comma}{selectedFields[index].ToSelectSql()}");
        }

        var root = keptSources.FirstOrDefault() ?? cte.Sources.FirstOrDefault();
        if (root is not null)
        {
            sb.AppendLine($"{indent}    FROM {root.DisplayText}");
        }

        foreach (var join in keptJoins)
        {
            sb.AppendLine($"{indent}    {join.DisplayText}");
        }

        AppendWhereClause(sb, cte.WhereClauseText, $"{indent}    ");

        sb.AppendLine($"{indent}) AS {alias}");
    }

    private static void AppendWhereClause(StringBuilder sb, string whereClauseText, string indent)
    {
        if (!string.IsNullOrWhiteSpace(whereClauseText))
        {
            sb.AppendLine($"{indent}WHERE {whereClauseText}");
        }
    }

    private static void AppendStatementTerminator(StringBuilder sb)
    {
        var sql = sb.ToString().TrimEnd();
        if (!sql.EndsWith(';'))
        {
            sb.Clear();
            sb.Append(sql);
            sb.AppendLine(";");
        }
    }

    private static string BuildDerivedJoinPrefix(JoinRef join)
    {
        return string.IsNullOrWhiteSpace(join.ConditionText)
            ? join.JoinType
            : $"{join.JoinType} JOIN";
    }

    private static HashSet<string> ResolveRequiredAliases(CteDefinition cte, IReadOnlyList<CteField> selectedFields)
    {
        var required = ResolveJoinRequiredAliases(cte, selectedFields);
        foreach (var alias in ResolveWhereRequiredAliases(cte))
        {
            required.Add(alias);
        }

        return required;
    }

    private static HashSet<string> ResolveJoinRequiredAliases(CteDefinition cte, IReadOnlyList<CteField> selectedFields)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cte.Sources.Count > 0)
        {
            required.Add(cte.Sources[0].Alias);
        }

        foreach (var field in selectedFields)
        {
            foreach (var reference in field.ColumnRefs)
            {
                required.Add(reference.Alias);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var join in cte.Joins)
            {
                if (!required.Contains(join.RightAlias))
                {
                    continue;
                }

                foreach (var alias in join.ColumnRefs.Select(reference => reference.Alias))
                {
                    if (required.Add(alias))
                    {
                        changed = true;
                    }
                }
            }
        }

        return required;
    }

    private static HashSet<string> ResolveWhereRequiredAliases(CteDefinition cte)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cte.Sources.Count > 0)
        {
            required.Add(cte.Sources[0].Alias);
        }

        foreach (var reference in cte.WhereColumnRefs)
        {
            required.Add(reference.Alias);
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var join in cte.Joins)
            {
                if (!required.Contains(join.RightAlias))
                {
                    continue;
                }

                foreach (var alias in join.ColumnRefs.Select(reference => reference.Alias))
                {
                    if (required.Add(alias))
                    {
                        changed = true;
                    }
                }
            }
        }

        return required;
    }

    private static List<string> BuildTrimmedFinalFields(CteParseResult result, IReadOnlySet<string> selectedFieldKeys)
    {
        var cteToSource = result.FinalQuery.Sources
            .Where(source => result.Ctes.Any(cte => string.Equals(cte.Name, source.ObjectName, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(source => source.ObjectName, source => source.Alias, StringComparer.OrdinalIgnoreCase);
        var fields = new List<string>();

        foreach (var cte in result.Ctes)
        {
            if (!cteToSource.TryGetValue(cte.Name, out var alias))
            {
                continue;
            }

            foreach (var field in cte.Fields.Where(field => selectedFieldKeys.Contains(field.Key)))
            {
                fields.Add($"{alias}.{field.Name}");
            }
        }

        if (fields.Count > 0)
        {
            return fields;
        }

        foreach (var cte in result.Ctes)
        {
            fields.AddRange(cte.Fields
                .Where(field => selectedFieldKeys.Contains(field.Key))
                .Select(field => $"{cte.Name}.{field.Name}"));
        }

        return fields.Count == 0 ? new List<string> { "*" } : fields;
    }

    private sealed class CteModelBuilder : TSqlFragmentVisitor
    {
        private readonly IList<TSqlParserToken> _tokens;

        public CteModelBuilder(IList<TSqlParserToken> tokens)
        {
            _tokens = tokens;
            Result = CteParseResult.Success();
        }

        public CteParseResult Result { get; }

        public override void ExplicitVisit(SelectStatement node)
        {
            if (node.WithCtesAndXmlNamespaces is not null)
            {
                foreach (var cte in node.WithCtesAndXmlNamespaces.CommonTableExpressions)
                {
                    Result.Ctes.Add(ParseCte(cte));
                }
            }

            Result.FinalQuery = ParseQueryBody("Final Select", node.QueryExpression, isCte: false);
        }

        private CteDefinition ParseCte(CommonTableExpression cte)
        {
            var definition = ParseQueryBody(cte.ExpressionName.Value, cte.QueryExpression, isCte: true);
            definition.SourceSql = GetFragmentText(cte);
            for (var index = 0; index < cte.Columns.Count && index < definition.Fields.Count; index++)
            {
                definition.Fields[index].Name = cte.Columns[index].Value;
            }

            return definition;
        }

        private CteDefinition ParseQueryBody(string name, QueryExpression queryExpression, bool isCte)
        {
            var definition = new CteDefinition { Name = name, IsCte = isCte };
            var spec = TryGetQuerySpecification(queryExpression);
            if (spec is null)
            {
                definition.Fields.Add(new CteField(
                    name,
                    "<unsupported query expression>",
                    "<unsupported query expression>",
                    null,
                    null,
                    false,
                    Array.Empty<ColumnRef>()));
                return definition;
            }

            foreach (var field in ExtractFields(name, spec))
            {
                definition.Fields.Add(field);
            }

            if (spec.FromClause is not null)
            {
                foreach (var tableReference in spec.FromClause.TableReferences)
                {
                    ParseTableReference(tableReference, definition);
                }
            }

            if (spec.WhereClause?.SearchCondition is not null)
            {
                definition.WhereClauseText = GetFragmentText(spec.WhereClause.SearchCondition);
                definition.WhereColumnRefs = ExtractColumnRefs(spec.WhereClause.SearchCondition);
            }

            return definition;
        }

        private List<CteField> ExtractFields(string cteName, QuerySpecification spec)
        {
            var fields = new List<CteField>();
            foreach (var element in spec.SelectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        fields.Add(ParseField(cteName, scalar));
                        break;
                    case SelectStarExpression:
                        fields.Add(new CteField(cteName, "*", "*", null, null, false, Array.Empty<ColumnRef>()));
                        break;
                    case SelectSetVariable variable:
                        var text = GetFragmentText(variable);
                        fields.Add(new CteField(cteName, text, text, null, null, false, Array.Empty<ColumnRef>()));
                        break;
                }
            }

            return fields;
        }

        private CteField ParseField(string cteName, SelectScalarExpression scalar)
        {
            var expressionText = GetFragmentText(scalar.Expression);
            var name = scalar.ColumnName?.Value;
            string? sourceAlias = null;
            string? sourceColumn = null;

            if (scalar.Expression is ColumnReferenceExpression columnReference)
            {
                var identifiers = columnReference.MultiPartIdentifier?.Identifiers;
                if (identifiers is not null && identifiers.Count > 0)
                {
                    sourceColumn = identifiers[^1].Value;
                    if (identifiers.Count >= 2)
                    {
                        sourceAlias = identifiers[^2].Value;
                    }

                    name ??= sourceColumn;
                }
            }

            name ??= expressionText;
            return new CteField(
                cteName,
                name,
                expressionText,
                sourceAlias,
                sourceColumn,
                scalar.ColumnName is not null,
                ExtractColumnRefs(scalar.Expression));
        }

        private SourceRef ParseTableReference(TableReference tableReference, CteDefinition definition)
        {
            switch (tableReference)
            {
                case QualifiedJoin join:
                    ParseTableReference(join.FirstTableReference, definition);
                    var right = ParseTableReference(join.SecondTableReference, definition);
                    var refs = ExtractJoinKeyRefs(join.SearchCondition);
                    var condition = join.SearchCondition is null ? "<no condition>" : GetFragmentText(join.SearchCondition);
                    var joinType = FormatQualifiedJoinType(join.QualifiedJoinType);
                    definition.Joins.Add(new JoinRef(
                        joinType,
                        right.Alias,
                        condition,
                        refs,
                        $"{joinType} JOIN {right.DisplayText} ON {condition}"));
                    return right;

                case UnqualifiedJoin join:
                    ParseTableReference(join.FirstTableReference, definition);
                    var unqualifiedRight = ParseTableReference(join.SecondTableReference, definition);
                    var unqualifiedJoinType = FormatUnqualifiedJoinType(join.UnqualifiedJoinType);
                    definition.Joins.Add(new JoinRef(
                        unqualifiedJoinType,
                        unqualifiedRight.Alias,
                        string.Empty,
                        new List<ColumnRef>(),
                        $"{unqualifiedJoinType} {unqualifiedRight.DisplayText}"));
                    return unqualifiedRight;

                case JoinParenthesisTableReference parenthesized:
                    return ParseTableReference(parenthesized.Join, definition);

                case NamedTableReference named:
                    var source = ParseNamedSource(named);
                    if (!definition.Sources.Any(existing => string.Equals(existing.Alias, source.Alias, StringComparison.OrdinalIgnoreCase)))
                    {
                        definition.Sources.Add(source);
                    }

                    return source;

                case QueryDerivedTable derived:
                    var alias = derived.Alias?.Value ?? "derived";
                    var derivedSource = new SourceRef(alias, "<derived>", $"<derived> AS {alias}");
                    definition.Sources.Add(derivedSource);
                    return derivedSource;

                default:
                    var text = GetFragmentText(tableReference);
                    var fallback = new SourceRef(text, text, text);
                    definition.Sources.Add(fallback);
                    return fallback;
            }
        }

        private static string FormatQualifiedJoinType(QualifiedJoinType joinType)
        {
            return joinType switch
            {
                QualifiedJoinType.Inner => "INNER",
                QualifiedJoinType.LeftOuter => "LEFT OUTER",
                QualifiedJoinType.RightOuter => "RIGHT OUTER",
                QualifiedJoinType.FullOuter => "FULL OUTER",
                _ => joinType.ToString().ToUpperInvariant()
            };
        }

        private static string FormatUnqualifiedJoinType(UnqualifiedJoinType joinType)
        {
            return joinType switch
            {
                UnqualifiedJoinType.CrossJoin => "CROSS JOIN",
                UnqualifiedJoinType.CrossApply => "CROSS APPLY",
                UnqualifiedJoinType.OuterApply => "OUTER APPLY",
                _ => joinType.ToString().ToUpperInvariant()
            };
        }

        private SourceRef ParseNamedSource(NamedTableReference node)
        {
            var objectName = string.Join(".", node.SchemaObject.Identifiers.Select(identifier => identifier.Value));
            var alias = node.Alias?.Value ?? node.SchemaObject.Identifiers.Last().Value;
            var displayText = string.Equals(alias, objectName, StringComparison.OrdinalIgnoreCase)
                ? objectName
                : $"{objectName} AS {alias}";
            return new SourceRef(alias, objectName, displayText);
        }

        private List<ColumnRef> ExtractColumnRefs(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return new List<ColumnRef>();
            }

            var visitor = new ColumnRefVisitor();
            fragment.Accept(visitor);
            return visitor.Refs;
        }

        private List<ColumnRef> ExtractJoinKeyRefs(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return new List<ColumnRef>();
            }

            var visitor = new JoinKeyRefVisitor();
            fragment.Accept(visitor);
            return visitor.Refs;
        }

        private string GetFragmentText(TSqlFragment fragment)
        {
            var sb = new StringBuilder();
            for (var index = fragment.FirstTokenIndex; index <= fragment.LastTokenIndex; index++)
            {
                sb.Append(_tokens[index].Text);
            }

            return sb.ToString().Trim();
        }

        private static QuerySpecification? TryGetQuerySpecification(QueryExpression queryExpression)
        {
            while (queryExpression is not null)
            {
                if (queryExpression is QuerySpecification spec)
                {
                    return spec;
                }

                if (queryExpression is QueryParenthesisExpression parenthesis)
                {
                    queryExpression = parenthesis.QueryExpression;
                    continue;
                }

                if (queryExpression is BinaryQueryExpression binary)
                {
                    queryExpression = binary.FirstQueryExpression;
                    continue;
                }

                break;
            }

            return null;
        }
    }

    private sealed class ColumnRefVisitor : TSqlFragmentVisitor
    {
        public List<ColumnRef> Refs { get; } = new();

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;
            if (identifiers is null || identifiers.Count < 2)
            {
                return;
            }

            Refs.Add(new ColumnRef(identifiers[^2].Value, identifiers[^1].Value));
        }
    }

    private sealed class JoinKeyRefVisitor : TSqlFragmentVisitor
    {
        public List<ColumnRef> Refs { get; } = new();

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
            if (node.ComparisonType != BooleanComparisonType.Equals)
            {
                return;
            }

            var first = TryGetColumnRef(node.FirstExpression);
            var second = TryGetColumnRef(node.SecondExpression);
            if (first is null || second is null)
            {
                return;
            }

            Refs.Add(first);
            Refs.Add(second);
        }

        private static ColumnRef? TryGetColumnRef(ScalarExpression expression)
        {
            if (expression is not ColumnReferenceExpression column)
            {
                return null;
            }

            var identifiers = column.MultiPartIdentifier?.Identifiers;
            if (identifiers is null || identifiers.Count < 2)
            {
                return null;
            }

            return new ColumnRef(identifiers[^2].Value, identifiers[^1].Value);
        }
    }
}

public sealed class CteParseResult
{
    private CteParseResult(bool isSuccess, string errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public string ErrorMessage { get; }

    public List<CteDefinition> Ctes { get; } = new();

    public CteDefinition FinalQuery { get; set; } = new() { Name = "Final Select" };

    public bool WasPlainSelectWrapped { get; set; }

    public static CteParseResult Success()
    {
        return new CteParseResult(true, string.Empty);
    }

    public static CteParseResult Failed(string errorMessage)
    {
        return new CteParseResult(false, errorMessage);
    }
}

public sealed class CteDefinition
{
    public string Name { get; init; } = string.Empty;

    public bool IsCte { get; init; }

    public string SourceSql { get; set; } = string.Empty;

    public List<CteField> Fields { get; } = new();

    public List<SourceRef> Sources { get; } = new();

    public List<JoinRef> Joins { get; } = new();

    public string WhereClauseText { get; set; } = string.Empty;

    public IReadOnlyList<ColumnRef> WhereColumnRefs { get; set; } = Array.Empty<ColumnRef>();
}

public sealed class CteField
{
    public CteField(
        string cteName,
        string name,
        string expressionText,
        string? sourceAlias,
        string? sourceColumn,
        bool hasExplicitAlias,
        IReadOnlyList<ColumnRef> columnRefs)
    {
        CteName = cteName;
        Name = name;
        ExpressionText = expressionText;
        SourceAlias = sourceAlias;
        SourceColumn = sourceColumn;
        HasExplicitAlias = hasExplicitAlias;
        ColumnRefs = columnRefs;
    }

    public string CteName { get; }

    public string Name { get; set; }

    public string ExpressionText { get; }

    public string? SourceAlias { get; }

    public string? SourceColumn { get; }

    public bool HasExplicitAlias { get; }

    public IReadOnlyList<ColumnRef> ColumnRefs { get; }

    public string Key => MakeKey(CteName, Name);

    public static string MakeKey(string cteName, string fieldName)
    {
        return $"{cteName}.{fieldName}";
    }

    public string ToSelectSql()
    {
        if (HasExplicitAlias || !string.Equals(Name, SourceColumn, StringComparison.OrdinalIgnoreCase))
        {
            return $"{ExpressionText} AS {Name}";
        }

        return ExpressionText;
    }
}

public sealed record SourceRef(string Alias, string ObjectName, string DisplayText);

public sealed record TrimmedCteDefinition(
    string Name,
    string Sql,
    bool IsKept,
    int OutputFieldCount,
    int KeptFieldCount,
    int TotalFieldCount,
    IReadOnlyList<TrimmedSqlLine> Lines);

public sealed record TrimmedSqlLine(string Text, TrimmedSqlLineState State);

public enum TrimmedSqlLineState
{
    Removed,
    Required,
    RequiredByWhere,
    RequiredByJoinAndWhere,
    Selected
}

public sealed record JoinRef(
    string JoinType,
    string RightAlias,
    string ConditionText,
    IReadOnlyList<ColumnRef> ColumnRefs,
    string DisplayText);

public sealed record ColumnRef(string Alias, string Column);

public sealed record CteValidationResult(bool IsValid, string Message);
