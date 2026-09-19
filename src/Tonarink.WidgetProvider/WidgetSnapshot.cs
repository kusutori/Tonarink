using System.Text.Json;
using Tonarink.Application;

namespace Tonarink.WidgetProvider;

internal static class WidgetSnapshot
{
    public const string NearbyPage = "nearby";
    public const string HistoryPage = "history";

    public static bool ServerIsOn()
    {
        if (!WidgetCommands.AppIsRunning())
            return false;

        return ReadSnapshot() is { ServerRunning: true };
    }

    public static bool ServerIsBusy() =>
        WidgetCommands.AppIsRunning() && ReadSnapshot() is { ServerBusy: true };

    public static string Capture(string page)
    {
        var appRunning = WidgetCommands.AppIsRunning();
        var snapshot = ReadSnapshot();
        var chrome = snapshot?.Chrome
                     ?? WidgetChromeCatalog.Resolve(snapshot?.Language ?? WidgetNative.UserLocale());
        var serverOn = appRunning && snapshot is { ServerRunning: true };
        var devices = appRunning && serverOn
            ? snapshot?.Devices ?? []
            : [];
        var history = ReadHistory();
        var transfer = appRunning ? snapshot?.Transfer : null;
        var hasTransfer = transfer is not null
            && !string.IsNullOrWhiteSpace(transfer.Title);
        var isNearby = !string.Equals(page, HistoryPage, StringComparison.Ordinal);
        var emptyLabel = !appRunning
            ? Text(chrome.EmptyAppClosed)
            : !serverOn
                ? Text(chrome.EmptyReceivingOff)
                : Text(chrome.EmptyNoDevices);

        var percent = transfer is null || transfer.TotalBytes <= 0
            ? 0
            : (int)Math.Clamp(transfer.BytesTransferred * 100d / transfer.TotalBytes, 0, 100);

        var data = new WidgetCardData
        {
            Title = "Tonarink",
            AppRunning = appRunning,
            AppStatusLabel = appRunning ? Text(chrome.AppStatusOpen) : Text(chrome.AppStatusClosed),
            StatusIcon = appRunning ? WidgetPaths.StatusOnIcon : WidgetPaths.StatusOffIcon,
            ServerOn = serverOn,
            ServerLabel = Text(chrome.ServerLabel),
            ServerValue = serverOn ? Text(chrome.ServerOn) : Text(chrome.ServerOff),
            ServerColor = serverOn ? "accent" : "default",
            ServerHint = !appRunning
                ? Text(chrome.HintAppClosed)
                : serverOn
                    ? Text(chrome.HintServerOn)
                    : Text(chrome.HintServerOff),
            IsNearby = isNearby,
            IsHistory = !isNearby,
            HasTransfer = hasTransfer,
            HasProgressBar = hasTransfer && transfer is { Indeterminate: false, TotalBytes: > 0 },
            HasDevices = devices.Count > 0,
            HasHistoryItems = history.Count > 0,
            DeviceCount = devices.Count,
            DeviceCountLabel = AppLanguages.Format(Text(chrome.NearbyCount), ("count", devices.Count)),
            HistoryCountLabel = AppLanguages.Format(Text(chrome.HistoryCount), ("count", history.Count)),
            EmptyLabel = emptyLabel,
            HistoryEmptyLabel = Text(chrome.HistoryEmpty),
            NearbyTab = Text(chrome.NearbyTab),
            HistoryTab = Text(chrome.HistoryTab),
            NearbyWeight = isNearby ? "bolder" : "default",
            HistoryWeight = isNearby ? "default" : "bolder",
            NearbyColor = isNearby ? "accent" : "default",
            HistoryColor = isNearby ? "default" : "accent",
            TransferTitle = transfer?.Title ?? "",
            TransferPeer = transfer is null
                ? ""
                : AppLanguages.Format(
                    Text(transfer.Incoming ? chrome.FromPeer : chrome.ToPeer),
                    ("peer", transfer.Peer)),
            TransferStatus = transfer?.Status ?? "",
            TransferProgress = transfer is null
                ? ""
                : transfer.Indeterminate || transfer.TotalBytes <= 0
                    ? ""
                    : $"{FormatBytes(transfer.BytesTransferred)} / {FormatBytes(transfer.TotalBytes)}  {percent}%",
            Devices = devices.Take(8).Select(static device => new WidgetCardRow
            {
                Alias = string.IsNullOrWhiteSpace(device.Alias) ? "?" : device.Alias.Trim(),
                Icon = WidgetPaths.DeviceIcon(device.Type),
            }).ToList(),
            History = history.Take(8).Select(item => new WidgetCardRow
            {
                FileName = item.FileName,
                Detail = string.IsNullOrWhiteSpace(item.Detail) ? item.FileName : item.Detail,
            }).ToList(),
        };

        return JsonSerializer.Serialize(data, WidgetJsonContext.Default.WidgetCardData);
    }

    private static WidgetSnapshotFile? ReadSnapshot()
    {
        try
        {
            var path = WidgetPaths.SnapshotPath();
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize(
                File.ReadAllText(path),
                WidgetJsonContext.Default.WidgetSnapshotFile);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            WidgetLog.Write($"Could not read widget snapshot: {exception.Message}");
            return null;
        }
    }

    private static IReadOnlyList<(string FileName, string Detail)> ReadHistory()
    {
        try
        {
            var path = WidgetPaths.HistoryPath();
            if (!File.Exists(path))
                return [];

            var file = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                WidgetJsonContext.Default.ReceiveHistoryFile);
            if (file?.Items is not { Count: > 0 })
                return [];

            return file.Items
                .Select(static item =>
                {
                    var name = string.IsNullOrWhiteSpace(item.FileName) ? "?" : item.FileName.Trim();
                    var sender = string.IsNullOrWhiteSpace(item.SenderAlias) ? "?" : item.SenderAlias.Trim();
                    var when = item.ReceivedAt == default
                        ? ""
                        : item.ReceivedAt.ToLocalTime().ToString("MM-dd HH:mm");
                    var detail = string.IsNullOrEmpty(when) ? sender : $"{sender}  ·  {when}";
                    return (name, detail);
                })
                .ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            WidgetLog.Write($"Could not read receive history for the widget: {exception.Message}");
            return [];
        }
    }

    private static string Text(string? value) => value ?? "";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
