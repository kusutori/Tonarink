using System.Text.Json;

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
        var chinese = IsChinese(snapshot?.Language);
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
            ? (chinese ? "应用未开启" : "App is closed")
            : !serverOn
                ? (chinese ? "接收已停止" : "Receiving is off")
                : (chinese ? "无设备" : "No devices");

        var percent = transfer is null || transfer.TotalBytes <= 0
            ? 0
            : (int)Math.Clamp(transfer.BytesTransferred * 100d / transfer.TotalBytes, 0, 100);

        var data = new WidgetCardData
        {
            Title = "Tonarink",
            AppRunning = appRunning,
            AppStatusLabel = appRunning
                ? (chinese ? "已开启" : "Running")
                : (chinese ? "未开启" : "Closed"),
            StatusIcon = appRunning ? WidgetPaths.StatusOnIcon : WidgetPaths.StatusOffIcon,
            ServerOn = serverOn,
            ServerLabel = chinese ? "接收服务" : "Receive",
            ServerValue = serverOn ? (chinese ? "开" : "On") : (chinese ? "关" : "Off"),
            ServerHint = !appRunning
                ? (chinese ? "打开应用后才能发现设备和接收文件。" : "Open the app to discover devices and receive files.")
                : serverOn
                    ? (chinese ? "附近设备可以发送文件。" : "Nearby devices can send files.")
                    : (chinese ? "点击开启接收。" : "Tap to start receiving."),
            IsNearby = isNearby,
            IsHistory = !isNearby,
            HasTransfer = hasTransfer,
            HasProgressBar = hasTransfer && transfer is { Indeterminate: false, TotalBytes: > 0 },
            HasDevices = devices.Count > 0,
            HasHistoryItems = history.Count > 0,
            DeviceCount = devices.Count,
            DeviceCountLabel = chinese
                ? $"附近 {devices.Count} 台"
                : $"{devices.Count} nearby",
            HistoryCountLabel = chinese
                ? $"历史 {history.Count} 条"
                : $"{history.Count} in history",
            EmptyLabel = emptyLabel,
            HistoryEmptyLabel = chinese ? "无历史记录" : "No history",
            NearbyTab = chinese ? "附近" : "Nearby",
            HistoryTab = chinese ? "历史" : "History",
            NearbyWeight = isNearby ? "bolder" : "default",
            HistoryWeight = isNearby ? "default" : "bolder",
            OpenLabel = chinese ? "打开 Tonarink" : "Open Tonarink",
            TransferTitle = transfer?.Title ?? "",
            TransferPeer = transfer is null
                ? ""
                : transfer.Incoming
                    ? (chinese ? $"来自 {transfer.Peer}" : $"From {transfer.Peer}")
                    : (chinese ? $"发送到 {transfer.Peer}" : $"To {transfer.Peer}"),
            TransferStatus = transfer?.Status ?? "",
            TransferProgress = transfer is null
                ? ""
                : transfer.Indeterminate || transfer.TotalBytes <= 0
                    ? ""
                    : $"{FormatBytes(transfer.BytesTransferred)} / {FormatBytes(transfer.TotalBytes)}  {percent}%",
            ProgressFilled = Math.Max(percent, 1),
            ProgressRest = Math.Max(100 - percent, 1),
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

    private static bool IsChinese(string? language) =>
        !string.IsNullOrWhiteSpace(language)
            ? language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            : WidgetNative.UserLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase);

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
