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
        if (ArchiveExtensions.Contains(extension))
            return ArchiveGlyph;
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return PdfGlyph;
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return TextGlyph;
        if (VideoExtensions.Contains(extension))
            return VideoGlyph;
        if (ImageExtensions.Contains(extension))
            return ImageGlyph;
        if (AudioExtensions.Contains(extension))
            return AudioGlyph;

        return "Document";
    }
}
