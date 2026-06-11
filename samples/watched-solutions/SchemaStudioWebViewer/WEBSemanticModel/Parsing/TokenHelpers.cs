using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text;

namespace SchemaStudioWebViewer.WEBSemanticModel.Parsing
{
    public static class TokenHelpers
    {
        public static string GetFragmentText(
            TSqlFragment fragment,
            IList<TSqlParserToken> tokens)
        {
            var sb = new StringBuilder();

            for (int i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
                sb.Append(tokens[i].Text);

            return sb.ToString();
        }
    }
}

