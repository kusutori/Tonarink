namespace Tonarink.Utilities;

enum FileTypeGlyphKind
{
    Document,
    Folder,
    Archive,
    Pdf,
    Text,
    Video,
    Image,
    Audio,
    Email,
    Calendar,
    OfflineMap,
    Database,
    Font,
    Ebook,
    Contact,
    Subtitle,
}

static class FileTypeGlyphs
{
    private const string DocumentGlyph = "\uE8A5";
    private const string FolderGlyph = "\uE8B7";
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

    public static string ForFileName(string? fileName) => ForKind(KindForFileName(fileName));

    public static string ForPath(string path) =>
        ForKind(Directory.Exists(path) ? FileTypeGlyphKind.Folder : KindForFileName(path));

    public static string ForKind(FileTypeGlyphKind kind) => kind switch
    {
        FileTypeGlyphKind.Folder => FolderGlyph,
        FileTypeGlyphKind.Archive => ArchiveGlyph,
        FileTypeGlyphKind.Pdf => PdfGlyph,
        FileTypeGlyphKind.Text => TextGlyph,
        FileTypeGlyphKind.Video => VideoGlyph,
        FileTypeGlyphKind.Image => ImageGlyph,
        FileTypeGlyphKind.Audio => AudioGlyph,
        FileTypeGlyphKind.Email => EmailGlyph,
        FileTypeGlyphKind.Calendar => CalendarGlyph,
        FileTypeGlyphKind.OfflineMap => OfflineMapGlyph,
        FileTypeGlyphKind.Database => DatabaseGlyph,
        FileTypeGlyphKind.Font => FontGlyph,
        FileTypeGlyphKind.Ebook => EbookGlyph,
        FileTypeGlyphKind.Contact => ContactGlyph,
        FileTypeGlyphKind.Subtitle => SubtitleGlyph,
        _ => DocumentGlyph,
    };

    public static FileTypeGlyphKind KindForFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return FileTypeGlyphKind.Document;

        var extension = Path.GetExtension(fileName);
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => FileTypeGlyphKind.Pdf,
            ".txt" => FileTypeGlyphKind.Text,
            var ext when ArchiveExtensions.Contains(ext) => FileTypeGlyphKind.Archive,
            var ext when VideoExtensions.Contains(ext) => FileTypeGlyphKind.Video,
            var ext when ImageExtensions.Contains(ext) => FileTypeGlyphKind.Image,
            var ext when AudioExtensions.Contains(ext) => FileTypeGlyphKind.Audio,
            var ext when EmailExtensions.Contains(ext) => FileTypeGlyphKind.Email,
            var ext when CalendarExtensions.Contains(ext) => FileTypeGlyphKind.Calendar,
            var ext when OfflineMapExtensions.Contains(ext) => FileTypeGlyphKind.OfflineMap,
            var ext when DatabaseExtensions.Contains(ext) => FileTypeGlyphKind.Database,
            var ext when FontExtensions.Contains(ext) => FileTypeGlyphKind.Font,
            var ext when EbookExtensions.Contains(ext) => FileTypeGlyphKind.Ebook,
            ".vcf" or ".vcard" => FileTypeGlyphKind.Contact,
            var ext when SubtitleExtensions.Contains(ext) => FileTypeGlyphKind.Subtitle,
            _ => FileTypeGlyphKind.Document,
        };
    }
}
