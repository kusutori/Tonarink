using System.Text;

namespace Tonarink.Pages.Send;

static class SelectedSendItemOperations
{
    public static SelectedSendItem Rename(SelectedSendItem item, string leafName)
    {
        var originalFileName = item.OriginalFileName ?? item.Item.FileName;
        var originalDisplayName = item.OriginalDisplayName ?? item.DisplayName;
        var separator = item.Item.FileName.LastIndexOf('/');
        var protocolName = separator < 0
            ? leafName
            : $"{item.Item.FileName[..(separator + 1)]}{leafName}";
        var isRenamed = !string.Equals(protocolName, originalFileName, StringComparison.Ordinal);
        return item with
        {
            Item = item.Item with { FileName = protocolName },
            DisplayName = isRenamed ? protocolName : originalDisplayName,
            OriginalFileName = originalFileName,
            OriginalDisplayName = originalDisplayName,
            RedoFileName = null,
            RedoDisplayName = null,
        };
    }

    public static SelectedSendItem UndoRename(SelectedSendItem item) =>
        !item.IsRenamed || item.OriginalFileName is not { } originalFileName
            ? item
            : item with
            {
                Item = item.Item with { FileName = originalFileName },
                DisplayName = item.OriginalDisplayName ?? originalFileName,
                RedoFileName = item.Item.FileName,
                RedoDisplayName = item.DisplayName,
            };

    public static SelectedSendItem RedoRename(SelectedSendItem item) =>
        !item.CanRedoRename || item.RedoFileName is not { } redoFileName
            ? item
            : item with
            {
                Item = item.Item with { FileName = redoFileName },
                DisplayName = item.RedoDisplayName ?? redoFileName,
            };

    public static string ProtocolLeafName(string protocolName)
    {
        var separator = protocolName.LastIndexOf('/');
        return separator < 0 ? protocolName : protocolName[(separator + 1)..];
    }

    public static long TextLength(string value) => Encoding.UTF8.GetByteCount(value);
}
