namespace Tonarink.Utilities;

static class FileTypeGlyphs
{
    private const string ArchiveGlyph = "\uF012";
    private const string PdfGlyph = "\uEA90";
    private const string TextGlyph = "\uF000";
    private const string VideoGlyph = "\uE714";
    private const string ImageGlyph = "\uE8B9";
    private const string AudioGlyph = "\uE8D6";

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
            _ => "Document",
        };
    }
}
