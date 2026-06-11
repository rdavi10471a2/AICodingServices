namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    // 🔥 NEW CLASSES FOR CARDINALITY
    public enum JoinCardinality
    {
        Unknown,
        Lookup,    // N:1 (Joined on this table's PK)
        Extension  // 1:N or 1:1 (Joined on this table's FK)
    }

    public class JoinKey
    {
        public string LocalColumn { get; set; }
        public string RemoteExpression { get; set; }
        public JoinCardinality Cardinality { get; set; } = JoinCardinality.Unknown;
    }

    public class SourceTable
    {
        public SourceKind Kind { get; set; }
        public string Database { get; set; }
        public string Schema { get; set; }
        public string Table { get; set; }
        public string Alias { get; set; }
        public string ParentAlias { get; set; }
        public string JoinType { get; set; }
        public int ResolvedOrder { get; set; }
        public string JoinExpression { get; set; }

        //-----------------------------------------
        // 🔥 FIX: Changed from List<string> to List<JoinKey>
        //-----------------------------------------
        public List<JoinKey> JoinKeys { get; set; } = new();

        public ParsedQuery NestedQuery { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Database))
                return $"{Database}.{Schema}.{Table} ({Alias})";
            if (!string.IsNullOrWhiteSpace(Schema))
                return $"{Schema}.{Table} ({Alias})";
            return $"{Table} ({Alias})";
        }
    }
}
