using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text;

namespace SchemaStudioWebViewer.WEBSemanticModel.Parsing
{
    public static class SqlFormatter
    {
        public static bool NeedsFormatting(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return false;

            // cheap heuristic: single-line or poorly spaced SQL
            return !sql.Contains(Environment.NewLine);
        }

        public static string Format(IList<TSqlParserToken> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            int indent = 0;
            bool newLine = true;

            foreach (var token in tokens)
            {
                var text = token.Text;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                //-----------------------------------------
                // NEWLINE CONTROL
                //-----------------------------------------
                if (IsClauseKeyword(text))
                {
                    sb.AppendLine();
                    AppendIndent(sb, indent);
                    sb.Append(text);
                    sb.Append(" ");
                    newLine = false;
                    continue;
                }

                //-----------------------------------------
                // INDENT CONTROL
                //-----------------------------------------
                if (text == "(")
                {
                    sb.Append("(");
                    indent++;
                    sb.AppendLine();
                    newLine = true;
                    continue;
                }

                if (text == ")")
                {
                    indent = Math.Max(0, indent - 1);
                    sb.AppendLine();
                    AppendIndent(sb, indent);
                    sb.Append(")");
                    sb.Append(" ");
                    newLine = false;
                    continue;
                }

                //-----------------------------------------
                // DEFAULT TOKEN
                //-----------------------------------------
                if (newLine)
                {
                    AppendIndent(sb, indent);
                    newLine = false;
                }

                sb.Append(text);
                sb.Append(" ");
            }

            return sb.ToString();
        }

        private static void AppendIndent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++)
                sb.Append("    ");
        }

        private static bool IsClauseKeyword(string text)
        {
            switch (text.ToUpperInvariant())
            {
                case "SELECT":
                case "FROM":
                case "WHERE":
                case "JOIN":
                case "INNER":
                case "LEFT":
                case "RIGHT":
                case "FULL":
                case "GROUP":
                case "ORDER":
                case "HAVING":
                case "ON":
                    return true;
                default:
                    return false;
            }
        }
    }
}

