using System.Collections.Immutable;
using System.Text.RegularExpressions;

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

enum HistorySearchError
{
    None,
    InvalidPattern,
    Timeout,
}

sealed record HistoryFilterResult(
    IReadOnlyList<ReceiveHistoryEntry> Entries,
    HistorySearchError Error = HistorySearchError.None);

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

    public bool MatchCase { get; init; }
    public bool WholeWord { get; init; }
    public bool UseRegex { get; init; }

    public bool IsActive => Search.Length != 0 || FileTypes.Count != 0
                                               || TimeRange != HistoryTimeRange.All ||
                                               SortOrder != HistorySortOrder.Newest;

    public bool IsDateRangeValid => TimeRange != HistoryTimeRange.Custom
                                   || From is null || To is null || From <= To;

    public bool IsSearchValid => TryCreateSearchExpression(out _);

    public bool IsValid => IsDateRangeValid && IsSearchValid;

    public HistoryFilter Normalize() => this with
    {
        Search = SearchQuery,
        FileTypes = FileTypes.Intersect(AvailableFileTypes),
        From = TimeRange == HistoryTimeRange.Custom ? From : null,
        To = TimeRange == HistoryTimeRange.Custom ? To : null,
    };

    public HistoryFilterResult Apply(
        IReadOnlyList<ReceiveHistoryEntry> entries,
        DateOnly today)
    {
        var query = SearchQuery;
        if (!TryCreateSearchExpression(out var expression))
            return new([], HistorySearchError.InvalidPattern);

        var matching = entries.Where(entry =>
            (query.Length == 0
             || Matches(entry.FileName)
             || Matches(entry.SenderAlias)
             || Matches(entry.Path))
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
        try
        {
            return new(ordered.ThenBy(entry => entry.Id).ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            // Abort the whole query on the first timeout, rather than spending
            // another timeout on every remaining field or history entry.
            return new([], HistorySearchError.Timeout);
        }

        bool Matches(string value) => expression is not null
            ? expression.IsMatch(value)
            : value.Contains(query, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    // Whitespace can be meaningful in a regular expression.
    private string SearchQuery => UseRegex ? Search : Search.Trim();

    private bool TryCreateSearchExpression(out Regex? expression)
    {
        expression = null;
        var query = SearchQuery;
        if (query.Length == 0 || (!UseRegex && !WholeWord)) return true;

        var pattern = UseRegex ? query : Regex.Escape(query);
        if (WholeWord)
            pattern = $@"(?<!\w)(?:{pattern})(?!\w)";
        var options = RegexOptions.CultureInvariant;
        if (!MatchCase) options |= RegexOptions.IgnoreCase;
        try
        {
            expression = new Regex(pattern, options, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
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
