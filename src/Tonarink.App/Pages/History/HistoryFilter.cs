using System.Collections.Immutable;

namespace Tonarink.Pages.History;

enum HistoryTimeRange
{
    All,
    Today,
    LastSevenDays,
    LastThirtyDays,
    Custom,
}

enum HistorySortOrder
{
    Newest,
    Oldest,
    NameAscending,
    NameDescending,
    SizeAscending,
    SizeDescending,
}

sealed record HistoryFilter(
    string Search = "",
    HistoryTimeRange TimeRange = HistoryTimeRange.All,
    HistorySortOrder SortOrder = HistorySortOrder.Newest,
    DateOnly? From = null,
    DateOnly? To = null)
{
    public static HistoryFilter Default { get; } = new();

    // History stores saved paths, not the sender's clipboard/message provenance.
    // Only offer categories that KindForPath can actually identify.
    public static ImmutableArray<FileTypeGlyphKind> AvailableFileTypes { get; } =
    [
        .. Enum.GetValues<FileTypeGlyphKind>().Where(static kind =>
            kind is not (FileTypeGlyphKind.TextMessage or FileTypeGlyphKind.Clipboard)),
    ];

    // An empty set means all types; selected types are combined with OR.
    public ImmutableHashSet<FileTypeGlyphKind> FileTypes { get; init; } = [];

    public bool IsActive => Search.Length != 0 || FileTypes.Count != 0
                                               || TimeRange != HistoryTimeRange.All ||
                                               SortOrder != HistorySortOrder.Newest;

    public bool IsValid => TimeRange != HistoryTimeRange.Custom
                           || From is null || To is null || From <= To;

    public HistoryFilter Normalize() => this with
    {
        Search = Search.Trim(),
        FileTypes = FileTypes.Intersect(AvailableFileTypes),
        From = TimeRange == HistoryTimeRange.Custom ? From : null,
        To = TimeRange == HistoryTimeRange.Custom ? To : null,
    };

    public IReadOnlyList<ReceiveHistoryEntry> FilterEntries(
        IReadOnlyList<ReceiveHistoryEntry> entries,
        DateOnly today)
    {
        var query = Search.Trim();
        var matching = entries.Where(entry =>
            (query.Length == 0
             || entry.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
             || entry.SenderAlias.Contains(query, StringComparison.OrdinalIgnoreCase)
             || entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (FileTypes.Count == 0 || FileTypes.Contains(FileTypeGlyphs.KindForPath(entry.Path)))
            && MatchesDate(DateOnly.FromDateTime(entry.ReceivedAt.LocalDateTime), today));

        var ordered = SortOrder switch
        {
            HistorySortOrder.Newest => matching.OrderByDescending(entry => entry.ReceivedAt),
            HistorySortOrder.Oldest => matching.OrderBy(entry => entry.ReceivedAt),
            HistorySortOrder.NameAscending => matching.OrderBy(entry => entry.FileName,
                StringComparer.CurrentCultureIgnoreCase),
            HistorySortOrder.NameDescending => matching.OrderByDescending(entry => entry.FileName,
                StringComparer.CurrentCultureIgnoreCase),
            HistorySortOrder.SizeAscending => matching.OrderBy(entry => entry.Size),
            HistorySortOrder.SizeDescending => matching.OrderByDescending(entry => entry.Size),
        };
        return ordered.ThenBy(entry => entry.Id).ToArray();
    }

    private bool MatchesDate(DateOnly date, DateOnly today) => TimeRange switch
    {
        HistoryTimeRange.All => true,
        HistoryTimeRange.Today => date == today,
        HistoryTimeRange.LastSevenDays => date >= today.AddDays(-6) && date <= today,
        HistoryTimeRange.LastThirtyDays => date >= today.AddDays(-29) && date <= today,
        // Compare local calendar dates: the end date includes its entire day,
        // including days containing a daylight-saving transition.
        HistoryTimeRange.Custom => (From is null || date >= From) && (To is null || date <= To),
    };
}
