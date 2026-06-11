using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text;
using SchemaStudio.AIHelpers;

namespace SchemaStudio.Data.Models;

[FileVersion("1.1")]
[AIFileContext("SchemaStudio.Data/Models/SchemaObjectColumnDefinition.cs", "Defines the saved Schema Studio column record used by repositories and the Blazor maintenance grids. Display and description metadata on the user-facing fields are the source of truth for grid headers and tooltip help indicators.", Responsibilities = "Carries persisted physical lineage, semantic source identity, business metadata, and inheritance flags for SchemaObjectColumn read/write workflows.", Nuances = "Base* fields describe physical lineage; Semantic* fields describe the semantic source/pass-through target that parser output resolved for the saved column.", RelatedFiles = "SchemaObjectColumnRepository, ManageViews.Columns, ViewSourcedColumnDefinition", LastReviewed = "2026-04-28")]
[AIChange("1.1", "2026-04-28 10:55 PM CDT added persisted Semantic* source fields to the writable SchemaObjectColumn model so parser semantic-source output can be saved with column metadata.", AICommandStatus.Pending)]
[AIChange("1.0", "2026-04-24 10:56 AM CDT added AI file metadata and completed the display/description attributes for the user-facing saved-column fields so UI grids can use model metadata directly without fallback help text.", AICommandStatus.Pending)]
public sealed class SchemaObjectColumnDefinition
{
    private int _schemaObjectColumnId;
    private int _schemaObjectId;
    private int _ordinalPosition;
    private string _sourceColumnName = "";
    private string? _sourceColumnKind;
    private string? _baseDatabaseName;
    private string? _baseSchemaName;
    private string? _baseObjectName;
    private string? _baseColumnName;
    private string? _semanticDatabase;
    private string? _semanticSchema;
    private string? _semanticObject;
    private string? _semanticColumn;
    private bool _isBaseDefinition;
    private bool _disableInheritance;
    private string? _businessName;
    private string? _businessDescription;
    private string? _developerNotes;
    private DateTime _lastSynced = DateTime.Now;
    private bool _isDirty;

    [Display(AutoGenerateField = false)]
    public int SchemaObjectColumnId
    {
        get => _schemaObjectColumnId;
        set => SetField(ref _schemaObjectColumnId, value);
    }

    [Required]
    [Range(1, int.MaxValue)]
    [Display(AutoGenerateField = false)]
    public int SchemaObjectId
    {
        get => _schemaObjectId;
        set => SetField(ref _schemaObjectId, value);
    }

    // 2026-04-28 10:55 PM CDT AI v1.1 marker: saved columns now persist Semantic* source identity alongside physical Base* lineage.
    // 2026-04-24 10:56 AM CDT AI v1.0 marker: the visible saved-column grid fields now have explicit display and description metadata so Razor headers can follow the parser-grid pattern without fallback text.
    [Required]
    [Display(Name = "Ordinal", Order = 10)]
    [Description("Ordinal position of the saved column within the current view definition.")]
    public int OrdinalPosition
    {
        get => _ordinalPosition;
        set => SetField(ref _ordinalPosition, value);
    }

    [Required]
    [StringLength(256)]
    [Display(Name = "Column", Order = 20)]
    [Description("Column name from the parsed source object.")]
    public string SourceColumnName
    {
        get => _sourceColumnName;
        set => SetField(ref _sourceColumnName, value);
    }

    [StringLength(64)]
    [Display(Name = "Source Column Kind", Order = 30)]
    [Description("Parsed column classification.")]
    public string? SourceColumnKind
    {
        get => _sourceColumnKind;
        set => SetField(ref _sourceColumnKind, value);
    }

    [StringLength(128)]
    [Display(Name = "Base Database", Order = 40)]
    public string? BaseDatabaseName
    {
        get => _baseDatabaseName;
        set => SetField(ref _baseDatabaseName, value);
    }

    [StringLength(128)]
    [Display(Name = "Base Schema", Order = 50)]
    public string? BaseSchemaName
    {
        get => _baseSchemaName;
        set => SetField(ref _baseSchemaName, value);
    }

    [StringLength(256)]
    [Display(Name = "Base Object", Order = 60)]
    public string? BaseObjectName
    {
        get => _baseObjectName;
        set => SetField(ref _baseObjectName, value);
    }

    [StringLength(256)]
    [Display(Name = "Base Column", Order = 70)]
    public string? BaseColumnName
    {
        get => _baseColumnName;
        set => SetField(ref _baseColumnName, value);
    }

    [StringLength(128)]
    [Display(Name = "Semantic Database", Order = 75)]
    [Description("Database for the saved column's semantic source/pass-through target.")]
    public string? SemanticDatabase
    {
        get => _semanticDatabase;
        set => SetField(ref _semanticDatabase, value);
    }

    [StringLength(128)]
    [Display(Name = "Semantic Schema", Order = 76)]
    [Description("Schema for the saved column's semantic source/pass-through target.")]
    public string? SemanticSchema
    {
        get => _semanticSchema;
        set => SetField(ref _semanticSchema, value);
    }

    [StringLength(128)]
    [Display(Name = "Semantic Object", Order = 77)]
    [Description("Object for the saved column's semantic source/pass-through target.")]
    public string? SemanticObject
    {
        get => _semanticObject;
        set => SetField(ref _semanticObject, value);
    }

    [StringLength(128)]
    [Display(Name = "Semantic Column", Order = 78)]
    [Description("Column for the saved column's semantic source/pass-through target.")]
    public string? SemanticColumn
    {
        get => _semanticColumn;
        set => SetField(ref _semanticColumn, value);
    }

    [Display(Name = "Base Definition", Order = 80)]
    public bool IsBaseDefinition
    {
        get => _isBaseDefinition;
        set => SetField(ref _isBaseDefinition, value);
    }

    [Display(Name = "Disable Inheritance", Order = 90)]
    [Description("Prevents inheritance-based refresh behavior from overriding the saved column's business metadata.")]
    public bool DisableInheritance
    {
        get => _disableInheritance;
        set => SetField(ref _disableInheritance, value);
    }

    [StringLength(128)]
    [Display(Name = "Business Name", Order = 100)]
    [Description("User-facing business label maintained for the saved Schema Studio column.")]
    public string? BusinessName
    {
        get => _businessName;
        set => SetField(ref _businessName, value);
    }

    [StringLength(500)]
    [Display(Name = "Business Description", Order = 110)]
    [Description("Business-facing description maintained for the saved Schema Studio column.")]
    public string? BusinessDescription
    {
        get => _businessDescription;
        set => SetField(ref _businessDescription, value);
    }

    [StringLength(3200)]
    [Display(Name = "Developer Notes", Order = 120)]
    [Description("Internal notes owned by the user and preserved independently from parser-driven synchronization.")]
    public string? DeveloperNotes
    {
        get => _developerNotes;
        set => SetField(ref _developerNotes, value);
    }

    [Display(Name = "Last Synced", AutoGenerateField = false)]
    public DateTime LastSynced
    {
        get => _lastSynced;
        set => SetField(ref _lastSynced, value);
    }

    [Display(AutoGenerateField = false)]
    public bool IsDirty
    {
        get => _isDirty;
        set => _isDirty = value;
    }

    [Display(AutoGenerateField = false)]
    public SchemaObjectColumnMergeState MergeState { get; set; } = SchemaObjectColumnMergeState.None;

    public void ClearDirty() => IsDirty = false;

    public string FormatDescription()
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine($"English Name: {BusinessName}");
        builder.AppendLine($"English Description: {BusinessDescription}");
        builder.AppendLine($"Developer Notes: {DeveloperNotes}");
        return builder.ToString();
    }

    public override string ToString() => string.IsNullOrWhiteSpace(BusinessName) ? SourceColumnName : BusinessName;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;

        if (propertyName != nameof(IsDirty))
        {
            IsDirty = true;
        }
    }
}

public enum SchemaObjectColumnMergeState
{
    None,
    DetectedAdd,
    DetectedRemove,
    PendingAdd,
    PendingUpdate,
    PendingRemove
}
