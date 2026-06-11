using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    // 🔥 Local semantic classification (intentionally redeclared here)
    //public enum ColumnKind
    //{
    //    Simple,
    //    Aggregate,
    //    Window,
    //    Expression
    //}

    [DefaultProperty("ColumnName")]
    [SchemaStudio.AIHelpers.FileVersion("1.1")]
    [SchemaStudio.AIHelpers.AIFileContext("WEBSemanticModel/Model/ViewSourcedColumnDefinition.cs", "Represents a parsed view column after projection resolution, including physical lineage, semantic source identity, and parser-carried business metadata before it is saved.", Responsibilities = "Carries DisableInheritance and Semantic* so semantic lookup overrides and lookup targets stay attached to parsed columns before later save flows materialize them into SchemaObjectColumnDefinition rows.", Nuances = "Preserve the distinction between physical Base* lineage and semantic Semantic* lookup identity; this model can carry parser flags without making all business metadata parser authoritative.", RelatedFiles = "ParsedQuery, ViewMetadataBinder, ManageViews.razor", LastReviewed = "2026-04-28")]
    [SchemaStudio.AIHelpers.AIChange("1.1", "2026-04-28 09:48 PM CDT added SemanticDatabase, SemanticSchema, SemanticObject, and SemanticColumn to parsed view columns so parser output can surface semantic lookup targets.", SchemaStudio.AIHelpers.AICommandStatus.Pending)]
    [SchemaStudio.AIHelpers.AIChange("1.0", "2026-04-25 12:18 PM CDT added DisableInheritance to the parsed view column model so the web save flow can initialize semantic override flags from parser metadata.", SchemaStudio.AIHelpers.AICommandStatus.Pending)]
    public class ViewSourcedColumnDefinition : INotifyPropertyChanged, IDataErrorInfo
    {
        // 2026-04-28 09:48 PM CDT AI v1.1 marker: parsed view columns now carry Semantic* lookup identity separately from Base* lineage.
        // 2026-04-25 12:18 PM CDT AI v1.0 marker: parsed view columns now carry DisableInheritance into the web save flow.
        public event PropertyChangedEventHandler PropertyChanged;

        private int _columnId;
        private int _tableId;
        private string _columnName;
        private int _ordinalPosition;

        // Resolved Projection Identity
        private string _database;
        private string _schema;
        private string _table;

        // Optional Physical Lineage
        private string _baseDatabase;
        private string _baseSchema;
        private string _baseTable;
        private string _baseColumn;

        // Optional Semantic Lookup Target
        private string _semanticDatabase;
        private string _semanticSchema;
        private string _semanticObject;
        private string _semanticColumn;

        // 🔥 NEW: Semantic classification
        private ColumnKind _columnKind;

        private string _businessName;
        private string _businessDescription;
        private string _developerNotes;
        private string _comment;

        private bool _disableInheritance;

        private DateTime _lastSynced = DateTime.Now;
        private bool _isDirty = false;

        #region IDataErrorInfo

        [Browsable(false)]
        public string Error => null;

        [Browsable(false)]
        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(ColumnName))
                {
                    bool hasBase =
                        !string.IsNullOrWhiteSpace(BaseDatabase) &&
                        !string.IsNullOrWhiteSpace(BaseSchema) &&
                        !string.IsNullOrWhiteSpace(BaseTable) &&
                        !string.IsNullOrWhiteSpace(BaseColumn);

                    bool hasProjection =
                        !string.IsNullOrWhiteSpace(Database) &&
                        !string.IsNullOrWhiteSpace(Schema) &&
                        !string.IsNullOrWhiteSpace(Table) &&
                        !string.IsNullOrWhiteSpace(ColumnName);

                    if (!hasBase && !hasProjection)
                    {
                        return "Column does not resolve to a physical column or a view projection.";
                    }
                }

                if (columnName == nameof(Database) ||
                    columnName == nameof(Schema) ||
                    columnName == nameof(Table))
                {
                    if (!string.IsNullOrWhiteSpace(ColumnName))
                    {
                        if (string.IsNullOrWhiteSpace(Database) ||
                            string.IsNullOrWhiteSpace(Schema) ||
                            string.IsNullOrWhiteSpace(Table))
                        {
                            return "Projection must have Database, Schema, and Table.";
                        }
                    }
                }

                return null;
            }
        }

        #endregion

        #region Identity

        [Browsable(false)]
        public int ColumnId
        {
            get => _columnId;
            set => SetField(ref _columnId, value);
        }

        [Browsable(false)]
        public int TableId
        {
            get => _tableId;
            set => SetField(ref _tableId, value);
        }

        [Category("Projection")]
        [DisplayName("Order")]
        [ReadOnly(true)]
        public int OrdinalPosition
        {
            get => _ordinalPosition;
            set => SetField(ref _ordinalPosition, value);
        }

        [Category("Projection")]
        [DisplayName("Column Name")]
        [Description("The projected column alias.")]
        public string ColumnName
        {
            get => _columnName;
            set => SetField(ref _columnName, value);
        }

        [Category("Projection")]
        [DisplayName("Database")]
        public string Database
        {
            get => _database;
            set => SetField(ref _database, value);
        }

        [Category("Projection")]
        [DisplayName("Schema")]
        public string Schema
        {
            get => _schema;
            set => SetField(ref _schema, value);
        }

        [Category("Projection")]
        [DisplayName("Object")]
        public string Table
        {
            get => _table;
            set => SetField(ref _table, value);
        }

        #endregion

        #region Semantics

        [Category("Semantics")]
        [DisplayName("Column Kind")]
        [Description("Semantic classification of the column (Simple, Aggregate, Window, Expression).")]
        public ColumnKind ColumnKind
        {
            get => _columnKind;
            set => SetField(ref _columnKind, value);
        }

        #endregion

        #region Physical Lineage (Optional)

        [Category("Source Lineage")]
        [DisplayName("Base Database")]
        public string BaseDatabase
        {
            get => _baseDatabase;
            set => SetField(ref _baseDatabase, value);
        }

        [Category("Source Lineage")]
        [DisplayName("Base Schema")]
        public string BaseSchema
        {
            get => _baseSchema;
            set => SetField(ref _baseSchema, value);
        }

        [Category("Source Lineage")]
        [DisplayName("Base Table")]
        public string BaseTable
        {
            get => _baseTable;
            set => SetField(ref _baseTable, value);
        }

        [Category("Source Lineage")]
        [DisplayName("Base Column")]
        public string BaseColumn
        {
            get => _baseColumn;
            set => SetField(ref _baseColumn, value);
        }

        #endregion

        #region Semantic Source (Optional)

        [Category("Semantic Source")]
        [DisplayName("Semantic Database")]
        public string SemanticDatabase
        {
            get => _semanticDatabase;
            set => SetField(ref _semanticDatabase, value);
        }

        [Category("Semantic Source")]
        [DisplayName("Semantic Schema")]
        public string SemanticSchema
        {
            get => _semanticSchema;
            set => SetField(ref _semanticSchema, value);
        }

        [Category("Semantic Source")]
        [DisplayName("Semantic Object")]
        public string SemanticObject
        {
            get => _semanticObject;
            set => SetField(ref _semanticObject, value);
        }

        [Category("Semantic Source")]
        [DisplayName("Semantic Column")]
        public string SemanticColumn
        {
            get => _semanticColumn;
            set => SetField(ref _semanticColumn, value);
        }

        #endregion

        #region Business Metadata

        //[Category("Business Metadata")]
        //[DisplayName("Inherit Base Metadata")]
        //[Description("If true, background syncs will pull descriptions/names from the base physical column if they are missing here.")]
        //public bool CanInheritBase
        //{
        //    get => _canInheritBase;
        //    set => SetField(ref _canInheritBase, value);
        //}

        [Category("Business Metadata")]
        [DisplayName("Business Name")]
        public string BusinessName
        {
            get => _businessName;
            set => SetField(ref _businessName, value);
        }

        [Category("Business Metadata")]
        [DisplayName("Description")]
        public string BusinessDescription
        {
            get => _businessDescription;
            set => SetField(ref _businessDescription, value);
        }

        [Category("Business Metadata")]
        [DisplayName("Developer Notes")]
        public string DeveloperNotes
        {
            get => _developerNotes;
            set => SetField(ref _developerNotes, value);
        }

        [Category("Business Metadata")]
        [DisplayName("Disable Inheritance")]
        [Description("Prevents inherited metadata from overriding the locally owned semantic meaning for this parsed column.")]
        public bool DisableInheritance
        {
            get => _disableInheritance;
            set => SetField(ref _disableInheritance, value);
        }

        [Category("Business Metadata")]
        [DisplayName("Comment")]
        public string Comment
        {
            get => _comment;
            set => SetField(ref _comment, value);
        }

        #endregion

        #region Tracking

        [Category("Tracking")]
        [DisplayName("Last Synced")]
        [ReadOnly(true)]
        public DateTime LastSynced
        {
            get => _lastSynced;
            set => SetField(ref _lastSynced, value);
        }

        [Browsable(false)]
        public bool isDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(BusinessName)
                ? ColumnName
                : BusinessName;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);

            if (propertyName != nameof(isDirty))
                isDirty = true;

            return true;
        }
    }
}
