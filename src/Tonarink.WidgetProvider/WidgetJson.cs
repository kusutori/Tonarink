using System.Text.Json.Serialization;

namespace Tonarink.WidgetProvider;

sealed class WidgetSnapshotFile
{
    public int Schema { get; set; }
    public bool ServerRunning { get; set; }
    public bool ServerBusy { get; set; }
    public bool ServerDesired { get; set; }
    public string? Alias { get; set; }
    public string? Language { get; set; }
    public List<WidgetDeviceFile>? Devices { get; set; }
    public WidgetTransferFile? Transfer { get; set; }
    public WidgetChromeFile? Chrome { get; set; }
}

sealed class WidgetChromeFile
{
    public string? AppStatusOpen { get; set; }
    public string? AppStatusClosed { get; set; }
    public string? ServerLabel { get; set; }
    public string? ServerOn { get; set; }
    public string? ServerOff { get; set; }
    public string? HintAppClosed { get; set; }
    public string? HintServerOn { get; set; }
    public string? HintServerOff { get; set; }
    public string? EmptyNoDevices { get; set; }
    public string? EmptyReceivingOff { get; set; }
    public string? EmptyAppClosed { get; set; }
    public string? HistoryEmpty { get; set; }
    public string? NearbyTab { get; set; }
    public string? HistoryTab { get; set; }
    public string? NearbyCount { get; set; }
    public string? HistoryCount { get; set; }
    public string? FromPeer { get; set; }
    public string? ToPeer { get; set; }
}

sealed class WidgetDeviceFile
{
    public string? Alias { get; set; }
    public string? Type { get; set; }
}

sealed class WidgetTransferFile
{
    public bool Incoming { get; set; }
    public string? Title { get; set; }
    public string? Peer { get; set; }
    public string? Status { get; set; }
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public bool Indeterminate { get; set; }
}

sealed class WidgetCommandFile
{
    public string? Verb { get; set; }
}

sealed class ReceiveHistoryFile
{
    public List<ReceiveHistoryItemFile>? Items { get; set; }
}

sealed class ReceiveHistoryItemFile
{
    public string? FileName { get; set; }
    public string? SenderAlias { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

sealed class WidgetCardData
{
    public string Title { get; set; } = "Tonarink";
    public bool AppRunning { get; set; }
    public string AppStatusLabel { get; set; } = "";
    public string StatusIcon { get; set; } = "";
    public bool ServerOn { get; set; }
    public string ServerLabel { get; set; } = "";
    public string ServerValue { get; set; } = "关";
    public string ServerColor { get; set; } = "default";
    public string ServerHint { get; set; } = "";
    public bool IsNearby { get; set; }
    public bool IsHistory { get; set; }
    public bool HasTransfer { get; set; }
    public bool HasProgressBar { get; set; }
    public bool HasDevices { get; set; }
    public bool HasHistoryItems { get; set; }
    public int DeviceCount { get; set; }
    public string DeviceCountLabel { get; set; } = "";
    public string HistoryCountLabel { get; set; } = "";
    public string EmptyLabel { get; set; } = "";
    public string HistoryEmptyLabel { get; set; } = "";
    public string NearbyTab { get; set; } = "";
    public string HistoryTab { get; set; } = "";
    public string NearbyWeight { get; set; } = "default";
    public string HistoryWeight { get; set; } = "default";
    public string NearbyColor { get; set; } = "default";
    public string HistoryColor { get; set; } = "default";
    public string TransferTitle { get; set; } = "";
    public string TransferPeer { get; set; } = "";
    public string TransferStatus { get; set; } = "";
    public string TransferProgress { get; set; } = "";
    public List<WidgetCardRow> Devices { get; set; } = [];
    public List<WidgetCardRow> History { get; set; } = [];
}

sealed class WidgetCardRow
{
    public string Alias { get; set; } = "";
    public string Icon { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Detail { get; set; } = "";
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WidgetSnapshotFile))]
[JsonSerializable(typeof(WidgetChromeFile))]
[JsonSerializable(typeof(WidgetCommandFile))]
[JsonSerializable(typeof(ReceiveHistoryFile))]
[JsonSerializable(typeof(WidgetCardData))]
[JsonSerializable(typeof(WidgetCardRow))]
[JsonSerializable(typeof(List<WidgetCardRow>))]
internal sealed partial class WidgetJsonContext : JsonSerializerContext;
