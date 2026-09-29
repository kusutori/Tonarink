using LocalSendDotNet;

namespace Tonarink.Pages.Send;

sealed record SelectedSendItem(
    Guid Id,
    SendItem Item,
    string DisplayName,
    long Length,
    string Kind,
    string? LocalPath = null,
    string? OriginalFileName = null,
    string? OriginalDisplayName = null,
    string? RedoFileName = null,
    string? RedoDisplayName = null)
{
    public bool IsRenamed => OriginalFileName is not null
                             && !string.Equals(Item.FileName, OriginalFileName, StringComparison.Ordinal);

    public bool CanRedoRename => RedoFileName is not null
                                 && !string.Equals(Item.FileName, RedoFileName, StringComparison.Ordinal);
}
