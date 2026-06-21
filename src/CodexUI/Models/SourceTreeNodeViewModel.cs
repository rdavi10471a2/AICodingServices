namespace CodexUI.Models;

public sealed record SourceTreeNodeViewModel(
    string Name,
    string? RelativePath,
    string Extension,
    bool IsFolder,
    bool IsSelected,
    bool IsSelectedAncestor,
    int Line,
    string Kind,
    IReadOnlyList<SourceTreeNodeViewModel> Children,
    IReadOnlyList<SourceOutlineNodeViewModel> Outline,
    string LinkKind = SourceTreeLinkKind.Watched);

public static class SourceTreeLinkKind
{
    public const string Watched = "watched";

    public const string Demo = "demo";
}
