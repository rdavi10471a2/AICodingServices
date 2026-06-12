namespace CodexUI.Models;

public sealed record SourceFileViewModel(
    string RelativePath,
    string FullPath,
    string Language,
    int SelectedLine,
    IReadOnlyList<string> Lines);
