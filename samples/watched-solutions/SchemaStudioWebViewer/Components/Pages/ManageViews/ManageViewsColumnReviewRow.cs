using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Components.Pages.ManageViews;

[FileVersion("1.0")]
[AIFileContext("Components/Pages/ManageViews/ManageViewsColumnReviewRow.cs", "Shared review-row model for the Manage Views synchronization workflow. This model carries parser-vs-saved row status into the merge dialog without forcing the dialog to depend on the page's internal state.", RelatedFiles = "Components/Pages/ManageViews/ManageViews.razor; Components/Pages/ManageViews/ManageViewsReviewMergeDialog.razor", LastReviewed = "2026-04-24")]
[AIChange("1.0", "2026-04-24 02:58 PM CDT introduced a shared manage-views review-row model so the page and merge dialog can exchange parser synchronization rows without relying on nested private page types.", AICommandStatus.Pending)]
public sealed class ManageViewsColumnReviewRow
{
    // 2026-04-24 02:58 PM CDT AI v1.0 marker: the merge dialog now reads shared review rows instead of relying on ManageViews' nested private type.
    public string ColumnName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
    public string ParsedPreview { get; set; } = string.Empty;
    public string ExistingPreview { get; set; } = string.Empty;
    public bool AcceptMerge { get; set; }
}
