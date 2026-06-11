using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using SchemaStudio.AIHelpers;

namespace SchemaStudio.Data.Models;

[FileVersion("1.0")]
[AIFileContext("SchemaStudio.Data/Models/SchemaObjectDefinition.cs", "Defines the saved Schema Studio object/view metadata record used by manage-views, base-view composition, and domain-object modeling workflows.", Responsibilities = "Carries the managed source object identity, base-view grain hint, business/domain metadata, composition definition JSON, and saved column collection.", Nuances = "SourceObjectName is the managed view/object name; SourceTableName is the physical table grain for base views and is intentionally required only when IsBaseObject is true.", LastReviewed = "2026-05-14")]
public sealed class SchemaObjectDefinition
{
    private int _schemaObjectId;
    private int _databaseId;
    private string? _sourceDatabaseName;
    private string _sourceSchemaName = "";
    private string? _sourceTableName;
    private string _sourceObjectName = "";
    private bool _isBaseObject;
    private string? _domain;
    private string? _businessName;
    private string? _businessDescription;
    private string? _developerNotes;
    private string? _compositionDefinitionJson;
    private bool _isActive = true;
    private DateTime _lastSynced = DateTime.Now;
    private bool _isDirty;
    private List<SchemaObjectColumnDefinition> _columns = new();

    [Display(AutoGenerateField = false)]
    public int SchemaObjectId
    {
        get => _schemaObjectId;
        set => SetField(ref _schemaObjectId, value);
    }

    [Required]
    [Range(1, int.MaxValue)]
    [Display(AutoGenerateField = false)]
    public int DatabaseId
    {
        get => _databaseId;
        set => SetField(ref _databaseId, value);
    }

    [StringLength(128)]
    [Display(Name = "Source Database", Order = 10)]
    [Description("Database containing the source view or object.")]
    public string? SourceDatabaseName
    {
        get => _sourceDatabaseName;
        set => SetField(ref _sourceDatabaseName, value);
    }

    [Required]
    [StringLength(128)]
    [Display(Name = "Source Schema", Order = 20)]
    [Description("Schema containing the source view or object.")]
    public string SourceSchemaName
    {
        get => _sourceSchemaName;
        set => SetField(ref _sourceSchemaName, value);
    }

    [StringLength(128)]
    [Display(Name = "Source Table", Order = 25)]
    [Description("Required for base views. Physical table whose grain this managed base view represents; only one base view can claim a table per database.")]
    public string? SourceTableName
    {
        get => _sourceTableName;
        set => SetField(ref _sourceTableName, value);
    }

    [Required]
    [StringLength(256)]
    [Display(Name = "Source Object", Order = 30)]
    [Description("Source view, table, or derived object name.")]
    public string SourceObjectName
    {
        get => _sourceObjectName;
        set => SetField(ref _sourceObjectName, value);
    }

    [Display(Name = "Base Object", Order = 40)]
    [Description("True when this record describes a base object rather than a derived view.")]
    public bool IsBaseObject
    {
        get => _isBaseObject;
        set => SetField(ref _isBaseObject, value);
    }

    [StringLength(128)]
    [Display(Name = "Domain", Order = 50)]
    [Description("Business domain grouping for this object.")]
    public string? Domain
    {
        get => _domain;
        set => SetField(ref _domain, value);
    }

    [StringLength(128)]
    [Display(Name = "Business Name", Order = 60)]
    [Description("Business-friendly name for this object.")]
    public string? BusinessName
    {
        get => _businessName;
        set => SetField(ref _businessName, value);
    }

    [StringLength(500)]
    [Display(Name = "Business Description", Order = 70)]
    [Description("Business context and usage for this object.")]
    public string? BusinessDescription
    {
        get => _businessDescription;
        set => SetField(ref _businessDescription, value);
    }

    [StringLength(3200)]
    [Display(Name = "Developer Notes", Order = 80)]
    [Description("Technical implementation notes for this object.")]
    public string? DeveloperNotes
    {
        get => _developerNotes;
        set => SetField(ref _developerNotes, value);
    }

    [StringLength(2500)]
    [Display(Name = "Composition Definition", Order = 90)]
    [Description("JSON recipe used to compose this derived domain object from managed base views.")]
    public string? CompositionDefinitionJson
    {
        get => _compositionDefinitionJson;
        set => SetField(ref _compositionDefinitionJson, value);
    }

    [Display(Name = "Active", Order = 100)]
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    [Display(Name = "Last Synced", AutoGenerateField = false)]
    public DateTime LastSynced
    {
        get => _lastSynced;
        set => SetField(ref _lastSynced, value);
    }

    [Display(AutoGenerateField = false)]
    public List<SchemaObjectColumnDefinition> Columns
    {
        get => _columns;
        set => SetField(ref _columns, value);
    }

    [Display(AutoGenerateField = false)]
    public bool IsDirty
    {
        get => _isDirty;
        set => _isDirty = value;
    }

    public void ClearDirty() => IsDirty = false;

    public override string ToString() => string.IsNullOrWhiteSpace(BusinessName) ? SourceObjectName : BusinessName;

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
