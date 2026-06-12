using AICodingServices.Data;

namespace CodexUI.Data.Repositories;

public sealed record WatchedSolutionIndexSnapshot(
    IReadOnlyList<IndexedProjectRow> Projects,
    IReadOnlyList<IndexedDocumentRow> Documents,
    IReadOnlyList<IndexedSymbolRow> Symbols);
