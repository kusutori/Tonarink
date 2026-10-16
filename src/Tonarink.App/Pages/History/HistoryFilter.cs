namespace Tonarink.Pages.History;

enum HistoryFileType
{
    All,
    Document,
    Image,
    Video,
    Audio,
    Archive,
    Other,
}

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
    HistoryFileType FileType = HistoryFileType.All,
    HistoryTimeRange TimeRange = HistoryTimeRange.All,
    HistorySortOrder SortOrder = HistorySortOrder.Newest,
    DateOnly? From = null,
    DateOnly? To = null)
{
    public static HistoryFilter Default { get; } = new();

    // The generic document glyph also represents unknown files; distinguish
    // ordinary text/office formats without inspecting whether files still exist.
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp",
        ".md", ".markdown", ".csv", ".tsv", ".log", ".json", ".xml", ".yaml", ".yml",
        ".html", ".htm", ".tex",
    };

    public bool IsActive => this != Default;

    public bool IsValid => TimeRange != HistoryTimeRange.Custom
                           || From is null || To is null || From <= To;

    public HistoryFilter Normalize() => this with
    {
        Search = Search.Trim(),
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
            && (FileType == HistoryFileType.All || Classify(entry.FileName) == FileType)
            && MatchesDate(DateOnly.FromDateTime(entry.ReceivedAt.LocalDateTime), today));

        var ordered = SortOrder switch
        {
            HistorySortOrder.Newest => matching.OrderByDescending(entry => entry.ReceivedAt),
            HistorySortOrder.Oldest => matching.OrderBy(entry => entry.ReceivedAt),
            HistorySortOrder.NameAscending => matching.OrderBy(entry => entry.FileName, StringComparer.CurrentCultureIgnoreCase),
            HistorySortOrder.NameDescending => matching.OrderByDescending(entry => entry.FileName, StringComparer.CurrentCultureIgnoreCase),
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

    private static HistoryFileType Classify(string fileName) => FileTypeGlyphs.KindForFileName(fileName) switch
    {
        FileTypeGlyphKind.Image => HistoryFileType.Image,
        FileTypeGlyphKind.Video => HistoryFileType.Video,
        FileTypeGlyphKind.Audio => HistoryFileType.Audio,
        FileTypeGlyphKind.Archive => HistoryFileType.Archive,
        FileTypeGlyphKind.Document => DocumentExtensions.Contains(Path.GetExtension(fileName))
            ? HistoryFileType.Document : HistoryFileType.Other,
        FileTypeGlyphKind.Pdf or FileTypeGlyphKind.Text or FileTypeGlyphKind.Ebook
            or FileTypeGlyphKind.Email or FileTypeGlyphKind.Calendar
            or FileTypeGlyphKind.Contact or FileTypeGlyphKind.Subtitle => HistoryFileType.Document,
        FileTypeGlyphKind.Folder or FileTypeGlyphKind.OfflineMap or FileTypeGlyphKind.Database
            or FileTypeGlyphKind.Font or FileTypeGlyphKind.TextMessage
            or FileTypeGlyphKind.Clipboard => HistoryFileType.Other,
    };
}
