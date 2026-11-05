namespace DiscRippa.Core.History;

/// <summary>The two "ripped" lists: DVDs / Blu-rays, and music CDs.</summary>
public sealed record HistoryStores(HistoryStore Videos, HistoryStore Music);
