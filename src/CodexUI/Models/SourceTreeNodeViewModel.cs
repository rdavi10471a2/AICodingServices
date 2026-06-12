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
    IReadOnlyList<SourceOutlineNodeViewModel> Outline);
