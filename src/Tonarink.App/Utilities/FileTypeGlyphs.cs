using System.Collections.Frozen;

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
    TextMessage,
    Clipboard,
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
    private const string TextMessageGlyph = "\uE70F";
    private const string ClipboardGlyph = "\uE77F";

    private static readonly string[] ArchiveExtensions =
    [
        ".7z", ".bz2", ".cab", ".gz", ".gzip", ".rar", ".tar", ".tbz", ".tbz2", ".tgz",
        ".txz", ".xz", ".zip", ".zst",
    ];

    private static readonly string[] VideoExtensions =
    [
        ".3g2", ".3gp", ".avi", ".flv", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg",
        ".mpg", ".mts", ".ogv", ".ts", ".webm", ".wmv",
    ];

    private static readonly string[] ImageExtensions =
    [
        ".avif", ".bmp", ".gif", ".heic", ".heif", ".ico", ".jfif", ".jpeg", ".jpg", ".png",
        ".svg", ".tif", ".tiff", ".webp",
    ];

    private static readonly string[] AudioExtensions =
    [
        ".aac", ".aif", ".aiff", ".alac", ".amr", ".flac", ".m4a", ".mp3", ".oga", ".ogg",
        ".opus", ".wav", ".wma",
    ];

    private static readonly string[] EmailExtensions =
    [
        ".eml", ".emlx", ".mbox", ".msg", ".ost", ".pst",
    ];

    private static readonly string[] CalendarExtensions =
    [
        ".ical", ".icalendar", ".ics", ".ifb",
    ];

    private static readonly string[] OfflineMapExtensions =
    [
        ".map", ".mbtiles", ".mwm", ".obf", ".osm", ".pbf", ".pmtiles",
    ];

    private static readonly string[] DatabaseExtensions =
    [
        ".accdb", ".db", ".db3", ".dbf", ".duckdb", ".fdb", ".mdb", ".mdf",
        ".realm", ".sqlite", ".sqlite3",
    ];

    private static readonly string[] FontExtensions =
    [
        ".eot", ".otc", ".otf", ".ttc", ".ttf", ".woff", ".woff2",
    ];

    private static readonly string[] EbookExtensions =
    [
        ".azw", ".azw3", ".cbr", ".cbz", ".djv", ".djvu", ".epub", ".fb2",
        ".kfx", ".lit", ".lrf", ".mobi", ".prc",
    ];

    private static readonly string[] SubtitleExtensions =
    [
        ".ass", ".srt", ".ssa", ".vtt",
    ];

    private static readonly string[] PdfExtensions = [".pdf"];
    private static readonly string[] TextExtensions = [".txt"];
    private static readonly string[] ContactExtensions = [".vcf", ".vcard"];

    // Classification, search and the representative suffix share one ordered
    // list per kind. Build a case-insensitive lookup once for file classification.
    private static readonly FrozenDictionary<string, FileTypeGlyphKind> KindsByExtension =
        Enum.GetValues<FileTypeGlyphKind>()
            .SelectMany(kind => ExtensionsForKind(kind).Select(extension => KeyValuePair.Create(extension, kind)))
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ExtensionsForKind(FileTypeGlyphKind kind) => kind switch
    {
        FileTypeGlyphKind.Archive => ArchiveExtensions,
        FileTypeGlyphKind.Pdf => PdfExtensions,
        FileTypeGlyphKind.Text => TextExtensions,
        FileTypeGlyphKind.Video => VideoExtensions,
        FileTypeGlyphKind.Image => ImageExtensions,
        FileTypeGlyphKind.Audio => AudioExtensions,
        FileTypeGlyphKind.Email => EmailExtensions,
        FileTypeGlyphKind.Calendar => CalendarExtensions,
        FileTypeGlyphKind.OfflineMap => OfflineMapExtensions,
        FileTypeGlyphKind.Database => DatabaseExtensions,
        FileTypeGlyphKind.Font => FontExtensions,
        FileTypeGlyphKind.Ebook => EbookExtensions,
        FileTypeGlyphKind.Contact => ContactExtensions,
        FileTypeGlyphKind.Subtitle => SubtitleExtensions,
        // Document is the fallback for unrecognized extensions, not an explicit
        // extension group; the remaining kinds describe non-file content.
        FileTypeGlyphKind.Document or FileTypeGlyphKind.Folder
            or FileTypeGlyphKind.TextMessage or FileTypeGlyphKind.Clipboard => [],
    };

    public static string ForFileName(string? fileName) => ForKind(KindForFileName(fileName));

    public static string ForPath(string path) => ForKind(KindForPath(path));

    public static FileTypeGlyphKind KindForPath(string path) =>
        Directory.Exists(path) ? FileTypeGlyphKind.Folder : KindForFileName(path);

    public static string ForSendItem(Pages.Send.SelectedSendItemKind kind, string? fileName) =>
        ForKind(KindForSendItem(kind, fileName));

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
        FileTypeGlyphKind.TextMessage => TextMessageGlyph,
        FileTypeGlyphKind.Clipboard => ClipboardGlyph,
        FileTypeGlyphKind.Document => DocumentGlyph,
    };

    public static FileTypeGlyphKind KindForSendItem(Pages.Send.SelectedSendItemKind kind, string? fileName) => kind switch
    {
        Pages.Send.SelectedSendItemKind.Text => FileTypeGlyphKind.TextMessage,
        Pages.Send.SelectedSendItemKind.Clipboard => FileTypeGlyphKind.Clipboard,
        Pages.Send.SelectedSendItemKind.File => KindForFileName(fileName),
    };

    public static FileTypeGlyphKind KindForFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return FileTypeGlyphKind.Document;

        var extension = Path.GetExtension(fileName);
        return KindsByExtension.GetValueOrDefault(extension, FileTypeGlyphKind.Document);
    }
}
