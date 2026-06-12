namespace CodexUI.Models;

public sealed record SourceOutlineNodeViewModel(
    string Label,
    string Kind,
    int Line,
    bool IsSelected);
