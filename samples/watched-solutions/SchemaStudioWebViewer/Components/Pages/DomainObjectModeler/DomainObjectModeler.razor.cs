using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.HelpSystem;
using SchemaStudioWebViewer.Utils;

[module: SchemaStudio.AIHelpers.AIFileContext(
    "Components/Pages/DomainObjectModeler/DomainObjectModeler.razor.cs",
    "Shared state and lifecycle for the Domain Object Modeler page.",
    Responsibilities = "Own page-level state, lifecycle loading, help helpers, status notification, clipboard copy, and small UI helpers.",
    RelatedFiles = "Components/Pages/DomainObjectModeler/DomainObjectModeler.razor",
    LastReviewed = "2026-05-13")]

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private const string HelpSubjectId = "domain-object-modeler";
    private const string DefaultTargetSchema = "dbo";
    private const string CteSourceModeInlineContents = "contents";
    private const string CteSourceModeViewSurface = "surface";
    private const int BaseViewPanelDefaultWidth = 360;
    private const int BaseViewPanelMinWidth = 280;
    private const int BaseViewPanelMaxWidth = 560;
    private const int ElementEditorDefaultWidth = 620;
    private const int ElementEditorMinWidth = 430;
    private const int ElementEditorMaxWidth = 860;

    private readonly string[] JoinTypes =
    [
        "LEFT JOIN",
        "INNER JOIN",
        "RIGHT JOIN",
        "FULL JOIN"
    ];

    private readonly CteSourceModeOption[] CteSourceModes =
    [
        new(CteSourceModeInlineContents, "CTE contents"),
        new(CteSourceModeViewSurface, "View surface")
    ];

    private List<DatabaseDefinition> Databases = new();
    private List<DatabaseDomainDefinition> Domains = new();
    private List<DomainBaseViewItem> BaseViews = new();
    private List<DomainObjectJoinRow> JoinRows = new();
    private List<SchemaStudioWebViewer.Data.TableSchemaForeignKeyEdge> SourceDatabaseRelationships = new();

    private int? SelectedDatabaseId;
    private string SelectedDomain = string.Empty;
    private string TargetSchema = DefaultTargetSchema;
    private string TargetViewName = string.Empty;
    private string GeneratedSql = string.Empty;
    private int GeneratedSqlVersion;
    private string StatusMessage = "Select a database and domain to begin.";
    private string LoadError = string.Empty;
    private bool IsBusy;
    private bool IsBaseViewPanelHidden;
    private bool StripSourceComments;
    private bool IsCompositionJsonVisible;
    private int NextSelectionOrdinal = 1;
    private bool ShouldHighlightSql;
    private ResizeTarget? ActiveResizeTarget;
    private double ResizeStartX;
    private int ResizeStartSize;
    private int BaseViewPanelWidth = BaseViewPanelDefaultWidth;
    private int ElementEditorWidth = ElementEditorDefaultWidth;

    private enum ResizeTarget
    {
        BaseViews,
        ElementEditor
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadDatabasesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!ShouldHighlightSql)
        {
            return;
        }

        ShouldHighlightSql = false;
        await JSRuntime.InvokeVoidAsync("highlightSql", 30);
    }

    private IReadOnlyList<DomainBaseViewItem> SelectedBaseViews =>
        BaseViews
            .Where(item => item.IsSelected)
            .OrderBy(item => item.SelectionOrdinal == 0 ? int.MaxValue : item.SelectionOrdinal)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private DatabaseDefinition? SelectedDatabase =>
        Databases.FirstOrDefault(database => database.DatabaseId == SelectedDatabaseId);

    private DomainBaseViewItem? AnchorView =>
        BaseViews.FirstOrDefault(item => item.IsSelected && item.IsAnchor);

    private IReadOnlyList<DomainBaseViewItem> NonAnchorSelectedBaseViews =>
        SelectedBaseViews.Where(item => !item.IsAnchor).ToList();

    private bool CanGenerate =>
        !IsBusy &&
        SelectedDatabaseId.HasValue &&
        SelectedBaseViews.Count > 0 &&
        AnchorView is not null &&
        !string.IsNullOrWhiteSpace(TargetSchema) &&
        !string.IsNullOrWhiteSpace(TargetViewName) &&
        NonAnchorSelectedBaseViews.All(item => !string.IsNullOrWhiteSpace(FindJoinRow(item.SchemaObjectId)?.OnClause));

    private string LayoutClass =>
        IsBaseViewPanelHidden
            ? "dom-shell dom-shell-collapsed"
            : "dom-shell";

    private string DomainObjectModelerClass =>
        ActiveResizeTarget is null ? "dom-page" : "dom-page dom-resizing";

    private string PaneResizeStyle =>
        $"--dom-base-width: {BaseViewPanelWidth}px; --dom-editor-width: {ElementEditorWidth}px;";

    private string ToggleBaseViewPanelText =>
        IsBaseViewPanelHidden ? "Show Base Views" : "Hide Base Views";

    private string ToggleCompositionJsonText =>
        IsCompositionJsonVisible ? "Hide Definition JSON" : "Show Definition JSON";

    private void ToggleBaseViewPanel()
    {
        IsBaseViewPanelHidden = !IsBaseViewPanelHidden;
    }

    private void ToggleCompositionJson()
    {
        IsCompositionJsonVisible = !IsCompositionJsonVisible;
    }

    private void BeginPaneResize(ResizeTarget target, PointerEventArgs args)
    {
        ActiveResizeTarget = target;
        ResizeStartX = args.ClientX;
        ResizeStartSize = target switch
        {
            ResizeTarget.BaseViews => BaseViewPanelWidth,
            ResizeTarget.ElementEditor => ElementEditorWidth,
            _ => 0
        };
    }

    private void ResizePane(PointerEventArgs args)
    {
        if (ActiveResizeTarget is null)
        {
            return;
        }

        var horizontalDelta = (int)Math.Round(args.ClientX - ResizeStartX);
        var nextWidth = ResizeStartSize + horizontalDelta;

        switch (ActiveResizeTarget)
        {
            case ResizeTarget.BaseViews:
                BaseViewPanelWidth = Math.Clamp(nextWidth, BaseViewPanelMinWidth, BaseViewPanelMaxWidth);
                break;
            case ResizeTarget.ElementEditor:
                ElementEditorWidth = Math.Clamp(nextWidth, ElementEditorMinWidth, ElementEditorMaxWidth);
                break;
        }
    }

    private void EndPaneResize()
    {
        ActiveResizeTarget = null;
    }

    private RenderFragment FieldLabel(Type modelType, string propertyName) => builder =>
    {
        var displayName = ReflectionUtils.GetDisplayName(modelType, propertyName);
        var description = ReflectionUtils.GetDescription(modelType, propertyName);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dom-field-label");
        builder.AddContent(2, displayName);

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AddContent(3, HelpIcon(description));
        }

        builder.CloseElement();
    };

    private RenderFragment HelpLabel(string label, string description) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dom-field-label");
        builder.AddContent(2, label);
        builder.AddContent(3, HelpIcon(description));
        builder.CloseElement();
    };

    private RenderFragment HelpIcon(string description) => builder =>
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        builder.OpenComponent<RadzenIcon>(0);
        builder.AddAttribute(1, "Icon", "help_outline");
        builder.AddAttribute(2, "class", "dom-help-icon");
        builder.AddAttribute(3, "MouseEnter", EventCallback.Factory.Create<ElementReference>(this, args => TooltipService.Open(args, description)));
        builder.CloseComponent();
    };

    private async Task OpenHelpAsync()
    {
        await DialogService.OpenAsync<HelpDialog>(
            "Schema Studio Help Console",
            new Dictionary<string, object?>
            {
                { "InitialSubjectId", HelpSubjectId }
            },
            new DialogOptions
            {
                Width = "1200px",
                Height = "800px",
                Resizable = true,
                Draggable = true
            });
    }

    private async Task CopySqlAsync()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql))
        {
            return;
        }

        await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", GeneratedSql);
        NotificationService.Notify(NotificationSeverity.Success, "SQL copied.", "", 3000);
    }

    private void NotifyFailure(string summary, Exception ex)
    {
        LoadError = ex.Message;
        NotificationService.Notify(NotificationSeverity.Error, summary, ex.Message, 6000);
    }

    private static bool ToBool(object? value) =>
        value switch
        {
            bool boolValue => boolValue,
            string textValue => bool.TryParse(textValue, out var parsed) && parsed,
            _ => false
        };
}


