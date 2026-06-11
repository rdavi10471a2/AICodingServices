using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaStudioWebViewer.WEBSemanticModel.Model;
using System.Diagnostics;

namespace SchemaStudioWebViewer.WEBSemanticModel.Parsing
{
    public class BasicSelectVisitor : TSqlFragmentVisitor
    {
        private readonly IList<TSqlParserToken> _tokens;

        private readonly Dictionary<string, (string Database, string Schema, string Table)> _aliasMap =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _registeredAliases =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly IReadOnlyDictionary<string, ParsedQuery> _cteMap;

        public ParsedQuery Result { get; } = new ParsedQuery();

        public BasicSelectVisitor(IList<TSqlParserToken> tokens)
            : this(tokens, new Dictionary<string, ParsedQuery>(StringComparer.OrdinalIgnoreCase))
        {
        }

        public BasicSelectVisitor(IList<TSqlParserToken> tokens, IReadOnlyDictionary<string, ParsedQuery> cteMap)
        {
            _tokens = tokens;
            _cteMap = cteMap;
        }

        public void Parse(QuerySpecification spec)
        {
            if (spec == null)
                throw new ArgumentNullException(nameof(spec));

            Debug.WriteLine("====================================================");
            Debug.WriteLine("VISITOR: STARTING TWO-PASS PARSE");
            Debug.WriteLine("====================================================");

            if (spec.FromClause != null)
            {
                foreach (var tableRef in spec.FromClause.TableReferences)
                {
                    ParseTableReference(tableRef, null, null, null);
                }
            }

            foreach (var selectElement in spec.SelectElements)
            {
                selectElement.Accept(this);
            }
        }

        public override void Visit(SelectScalarExpression node)
        {
            var expressionText = TokenHelpers.GetFragmentText(node.Expression, _tokens).Trim();
            var explicitAlias = node.ColumnName?.Value;
            var comment = GetTrailingComment(node);

            string sourceAlias = null;
            string schema = null;
            string table = null;
            string column = null;
            string database = null;

            if (node.Expression is ColumnReferenceExpression colRef)
            {
                var ids = colRef.MultiPartIdentifier?.Identifiers;
                if (ids != null)
                {
                    column = ids.Last().Value;
                    if (ids.Count == 2) { sourceAlias = ids[0].Value; }
                    else if (ids.Count == 3) { schema = ids[0].Value; table = ids[1].Value; }
                    else if (ids.Count == 4) { database = ids[0].Value; schema = ids[1].Value; table = ids[2].Value; }
                }
            }

            var kind = Classify(node.Expression);
            var resolvedAlias = explicitAlias ?? column;

            var item = new SelectItem
            {
                Alias = resolvedAlias,
                Expression = expressionText,
                ExpressionText = expressionText,
                ExpressionNode = node.Expression,
                Kind = kind,
                Comment = comment
            };

            item.Binding.SourceAlias = sourceAlias;
            item.Binding.SourceColumn = column ?? explicitAlias;

            if (sourceAlias != null && _aliasMap.TryGetValue(sourceAlias, out var src))
            {
                item.Binding.SourceDatabase = src.Database;
                item.Binding.SourceSchema = src.Schema;
                item.Binding.SourceTable = src.Table;
            }
            else
            {
                item.Binding.SourceDatabase = database;
                item.Binding.SourceSchema = schema;
                item.Binding.SourceTable = table;
            }

            Result.SelectItems.Add(item);
        }

        private void ParseTableReference(TableReference tableRef, string joinType, string joinExpr, BooleanExpression searchCondition)
        {
            if (tableRef is QualifiedJoin join)
            {
                ParseTableReference(join.FirstTableReference, null, null, null);
                var joinExprText = TokenHelpers.GetFragmentText(join.SearchCondition, _tokens).Trim();
                ParseTableReference(join.SecondTableReference, join.QualifiedJoinType.ToString(), joinExprText, join.SearchCondition);
            }
            else if (tableRef is NamedTableReference named)
            {
                var target = AddTable(named, joinType, joinExpr);
                if (target != null && searchCondition != null) ExtractJoinKeys(searchCondition, target);
            }
            else if (tableRef is JoinParenthesisTableReference parenJoin)
            {
                ParseTableReference(parenJoin.Join, joinType, joinExpr, searchCondition);
            }
            else if (tableRef is QueryDerivedTable derived)
            {
                var target = AddDerivedTable(derived, joinType, joinExpr);
                if (target != null && searchCondition != null) ExtractJoinKeys(searchCondition, target);
            }
        }
        private void ExtractJoinKeys(BooleanExpression condition, SourceTable targetTable)
        {
            if (condition == null) return;

            // 1. Handle AND (Multi-column keys)
            if (condition is BooleanBinaryExpression binary && binary.BinaryExpressionType == BooleanBinaryExpressionType.And)
            {
                ExtractJoinKeys(binary.FirstExpression, targetTable);
                ExtractJoinKeys(binary.SecondExpression, targetTable);
            }
            // 2. Handle Parentheses (Clean up the tree)
            else if (condition is BooleanParenthesisExpression paren)
            {
                ExtractJoinKeys(paren.Expression, targetTable);
            }
            // 3. The Actual Equality Check (The leaf node)
            else if (condition is BooleanComparisonExpression comparison &&
                     comparison.ComparisonType == BooleanComparisonType.Equals)
            {
                var left = Unwrap(comparison.FirstExpression);
                var right = Unwrap(comparison.SecondExpression);

                if (IsMatch(left, targetTable.Alias, out string localCol))
                {
                    targetTable.JoinKeys.Add(new JoinKey
                    {
                        LocalColumn = localCol,
                        RemoteExpression = TokenHelpers.GetFragmentText(right, _tokens)
                    });
                }
                else if (IsMatch(right, targetTable.Alias, out string localCol2))
                {
                    targetTable.JoinKeys.Add(new JoinKey
                    {
                        LocalColumn = localCol2,
                        RemoteExpression = TokenHelpers.GetFragmentText(left, _tokens)
                    });
                }
            }
        }


        //private void ExtractJoinKeys(BooleanExpression condition, SourceTable targetTable)
        //{
        //    if (condition is BooleanComparisonExpression comparison && comparison.ComparisonType == BooleanComparisonType.Equals)
        //    {
        //        var left = Unwrap(comparison.FirstExpression);
        //        var right = Unwrap(comparison.SecondExpression);

        //        if (IsMatch(left, targetTable.Alias, out string localCol))
        //        {
        //            targetTable.JoinKeys.Add(new JoinKey { LocalColumn = localCol, RemoteExpression = TokenHelpers.GetFragmentText(right, _tokens) });
        //        }
        //        else if (IsMatch(right, targetTable.Alias, out string localCol2))
        //        {
        //            targetTable.JoinKeys.Add(new JoinKey { LocalColumn = localCol2, RemoteExpression = TokenHelpers.GetFragmentText(left, _tokens) });
        //        }
        //    }
        //}

        private bool IsMatch(ScalarExpression expr, string targetAlias, out string columnName)
        {
            columnName = null;
            if (expr is ColumnReferenceExpression colRef && colRef.MultiPartIdentifier.Identifiers.Count >= 2)
            {
                var ids = colRef.MultiPartIdentifier.Identifiers;
                if (string.Equals(ids[^2].Value, targetAlias, StringComparison.OrdinalIgnoreCase))
                {
                    columnName = ids[^1].Value;
                    return true;
                }
            }
            return false;
        }

        private SourceTable AddTable(NamedTableReference node, string joinType, string joinExpr)
        {
            string db = null, sc = null, tbl = null;
            var ids = node.SchemaObject.Identifiers;
            if (ids.Count == 3) { db = ids[0].Value; sc = ids[1].Value; tbl = ids[2].Value; }
            else if (ids.Count >= 2) { sc = ids[^2].Value; tbl = ids[^1].Value; }
            else if (ids.Count == 1) { tbl = ids[0].Value; }

            var alias = node.Alias?.Value ?? tbl;
            if (!_registeredAliases.Add(alias)) return null;

            if (ids.Count == 1 && _cteMap.TryGetValue(tbl, out var cteQuery))
            {
                var cteSource = new SourceTable
                {
                    Kind = SourceKind.Cte,
                    Table = tbl,
                    Alias = alias,
                    JoinType = joinType,
                    JoinExpression = joinExpr,
                    NestedQuery = cteQuery
                };

                Result.SourceTables.Add(cteSource);
                _aliasMap[alias] = (null, null, tbl);
                return cteSource;
            }

            var st = new SourceTable { Kind = SourceKind.NamedObject, Database = db, Schema = sc, Table = tbl, Alias = alias, JoinType = joinType, JoinExpression = joinExpr };
            Result.SourceTables.Add(st);
            _aliasMap[alias] = (db, sc, tbl);
            return st;
        }

        private SourceTable AddDerivedTable(QueryDerivedTable node, string joinType, string joinExpr)
        {
            var alias = node.Alias?.Value ?? Guid.NewGuid().ToString("N");
            if (!_registeredAliases.Add(alias)) return null;

            ParsedQuery nested = null;
            var spec = TryGetQuerySpecification(node.QueryExpression);
            if (spec != null)
            {
                var v = new BasicSelectVisitor(_tokens, _cteMap);
                v.Parse(spec);
                nested = v.Result;
            }

            var st = new SourceTable { Kind = SourceKind.DerivedQuery, Alias = alias, JoinType = joinType, JoinExpression = joinExpr, NestedQuery = nested };
            Result.SourceTables.Add(st);
            _aliasMap[alias] = (null, null, null);
            return st;
        }

        private string GetTrailingComment(SelectScalarExpression node)
        {
            int currentEnd = node.StartOffset + node.FragmentLength;

            for (int i = 0; i < _tokens.Count; i++)
            {
                var t = _tokens[i];

                if (t.Offset < currentEnd)
                    continue;

                // Stop at clause boundaries (we've left the SELECT list)
                if (t.TokenType == TSqlTokenType.From ||
                    t.TokenType == TSqlTokenType.Where ||
                    t.TokenType == TSqlTokenType.Group ||
                    t.TokenType == TSqlTokenType.Having ||
                    t.TokenType == TSqlTokenType.Order)
                {
                    break;
                }

                // Ignore whitespace
                if (t.TokenType == TSqlTokenType.WhiteSpace)
                    continue;

                // Comma is allowed (comment may appear before OR after it)
                if (t.TokenType == TSqlTokenType.Comma)
                    continue;

                // First comment encountered in the window → this is the trailing comment
                if (t.TokenType == TSqlTokenType.SingleLineComment ||
                    t.TokenType == TSqlTokenType.MultilineComment)
                {
                    return t.Text.Trim();
                }

                // Any other token means we've hit the next select item → stop
                break;
            }

            return null;
        }

        public static QuerySpecification TryGetQuerySpecification(QueryExpression expr)
        {
            while (expr != null)
            {
                if (expr is QuerySpecification qs) return qs;
                if (expr is QueryParenthesisExpression paren) { expr = paren.QueryExpression; continue; }
                if (expr is BinaryQueryExpression bin) { expr = bin.FirstQueryExpression; continue; }
                break;
            }
            return null;
        }

        private static ColumnKind Classify(ScalarExpression expr)
        {
            if (expr == null) return ColumnKind.Expression;
            expr = Unwrap(expr);
            if (expr is ColumnReferenceExpression colRef && colRef.MultiPartIdentifier != null) return ColumnKind.Simple;
            if (ContainsOver(expr)) return ColumnKind.Window;
            if (expr is Microsoft.SqlServer.TransactSql.ScriptDom.BinaryExpression || expr is CaseExpression) return ColumnKind.Expression;
            if (expr is FunctionCall func) return IsAggregate(func) ? ColumnKind.Aggregate : ColumnKind.Expression;
            return ContainsAggregate(expr) ? ColumnKind.Aggregate : ColumnKind.Expression;
        }

        private static ScalarExpression Unwrap(ScalarExpression expr)
        {
            while (true)
            {
                if (expr is ParenthesisExpression p) { expr = p.Expression; continue; }
                if (expr is ConvertCall c) { expr = c.Parameter; continue; }
                return expr;
            }
        }

        private static bool IsAggregate(FunctionCall node)
        {
            var name = node.FunctionName?.Value?.ToUpperInvariant();
            return name == "SUM" || name == "COUNT" || name == "AVG" || name == "MIN" || name == "MAX";
        }

        private static bool ContainsAggregate(TSqlFragment expr) { var v = new AggVisitor(); expr.Accept(v); return v.Found; }
        private static bool ContainsOver(TSqlFragment expr) { var v = new OverVisitor(); expr.Accept(v); return v.Found; }
        private sealed class AggVisitor : TSqlFragmentVisitor { public bool Found; public override void Visit(FunctionCall node) { if (!Found && IsAggregate(node)) Found = true; } }
        private sealed class OverVisitor : TSqlFragmentVisitor { public bool Found; public override void Visit(OverClause node) { Found = true; } }
    }
}
