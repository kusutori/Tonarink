namespace Tonarink.Controls;

// TextMemberPath and the code-built template use WinRT binding.
// Generate that surface explicitly rather than relying on reflection in AOT.
[WinRT.GeneratedBindableCustomProperty]
public sealed partial class FileTypeToken
{
    internal FileTypeToken(FileTypeGlyphKind kind, string label)
    {
        Kind = kind;
        Label = label;
        Glyph = FileTypeGlyphs.ForKind(kind);
    }

    internal FileTypeGlyphKind Kind { get; }
    public string Label { get; }
    public string Glyph { get; }

    public override string ToString() => Label;
}
