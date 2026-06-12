namespace CodexUI.Models;

public sealed record SourceFileNodeViewModel(
    string RelativePath,
    string DisplayName,
    string Extension,
    bool IsSelected,
    IReadOnlyList<SourceOutlineNodeViewModel> Outline);
