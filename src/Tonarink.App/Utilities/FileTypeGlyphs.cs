namespace Tonarink.Utilities;

static class FileTypeGlyphs
{
    private const string ArchiveGlyph = "\uF012";
    private const string PdfGlyph = "\uEA90";
    private const string TextGlyph = "\uF000";
    private const string VideoGlyph = "\uE714";
    private const string ImageGlyph = "\uE8B9";
    private const string AudioGlyph = "\uE8D6";
    private const string EmailGlyph = "\uE715";
    private const string CalendarGlyph = "\uE787";
    private const string OfflineMapGlyph = "\uE800";
    private const string DatabaseGlyph = "\uEE94";
    private const string FontGlyph = "\uE8D2";
    private const string EbookGlyph = "\uE8F1";
    private const string ContactGlyph = "\uE716";
    private const string SubtitleGlyph = "\uED1E";

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".bz2", ".cab", ".gz", ".gzip", ".rar", ".tar", ".tbz", ".tbz2", ".tgz",
        ".txz", ".xz", ".zip", ".zst",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3g2", ".3gp", ".avi", ".flv", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg",
        ".mpg", ".mts", ".ogv", ".ts", ".webm", ".wmv",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".bmp", ".gif", ".heic", ".heif", ".ico", ".jfif", ".jpeg", ".jpg", ".png",
        ".svg", ".tif", ".tiff", ".webp",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".aif", ".aiff", ".alac", ".amr", ".flac", ".m4a", ".mp3", ".oga", ".ogg",
        ".opus", ".wav", ".wma",
    };

    private static readonly HashSet<string> EmailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".eml", ".emlx", ".mbox", ".msg", ".ost", ".pst",
    };

    private static readonly HashSet<string> CalendarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ical", ".icalendar", ".ics", ".ifb",
    };

    private static readonly HashSet<string> OfflineMapExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".map", ".mbtiles", ".mwm", ".obf", ".osm", ".pbf", ".pmtiles",
    };

    private static readonly HashSet<string> DatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".accdb", ".db", ".db3", ".dbf", ".duckdb", ".fdb", ".mdb", ".mdf",
        ".realm", ".sqlite", ".sqlite3",
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".eot", ".otc", ".otf", ".ttc", ".ttf", ".woff", ".woff2",
    };

    private static readonly HashSet<string> EbookExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".azw", ".azw3", ".cbr", ".cbz", ".djv", ".djvu", ".epub", ".fb2",
        ".kfx", ".lit", ".lrf", ".mobi", ".prc",
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass", ".srt", ".ssa", ".vtt",
    };

    public static string ForFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Document";

        var extension = Path.GetExtension(fileName);
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => PdfGlyph,
            ".txt" => TextGlyph,
            var ext when ArchiveExtensions.Contains(ext) => ArchiveGlyph,
            var ext when VideoExtensions.Contains(ext) => VideoGlyph,
            var ext when ImageExtensions.Contains(ext) => ImageGlyph,
            var ext when AudioExtensions.Contains(ext) => AudioGlyph,
            var ext when EmailExtensions.Contains(ext) => EmailGlyph,
            var ext when CalendarExtensions.Contains(ext) => CalendarGlyph,
            var ext when OfflineMapExtensions.Contains(ext) => OfflineMapGlyph,
            var ext when DatabaseExtensions.Contains(ext) => DatabaseGlyph,
            var ext when FontExtensions.Contains(ext) => FontGlyph,
            var ext when EbookExtensions.Contains(ext) => EbookGlyph,
            ".vcf" or ".vcard" => ContactGlyph,
            var ext when SubtitleExtensions.Contains(ext) => SubtitleGlyph,
            _ => "Document",
        };
    }
}
