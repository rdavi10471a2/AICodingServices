namespace AICodingServices.Data;

public sealed record IndexedSymbolSearchResult(
    IReadOnlyList<IndexedSymbolQueryItem> Symbols,
    int TotalSymbolCount,
    int MaxResults,
    bool LimitClamped);
