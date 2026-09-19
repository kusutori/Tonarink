using Tonarink.Application;

namespace Tonarink.WidgetProvider;

internal static class WidgetChromeCatalog
{
    public static WidgetChromeFile Resolve(string? language) => AppLanguages.Match(language) switch
    {
        AppLanguages.SimplifiedChinese => SimplifiedChinese,
        _ => English,
    };

    private static WidgetChromeFile English { get; } = new()
    {
        AppStatusOpen = "Running",
        AppStatusClosed = "Closed",
        ServerLabel = "Receive",
        ServerOn = "On",
        ServerOff = "Off",
        HintAppClosed = "Open the app to discover devices and receive files.",
        HintServerOn = "Nearby devices can send files.",
        HintServerOff = "Tap to start receiving.",
        EmptyNoDevices = "No nearby devices",
        EmptyReceivingOff = "Receiving is off",
        EmptyAppClosed = "App is closed",
        HistoryEmpty = "No history",
        NearbyTab = "Nearby",
        HistoryTab = "History",
        NearbyCount = "{count} nearby",
        HistoryCount = "{count} in history",
        FromPeer = "From {peer}",
        ToPeer = "To {peer}",
    };

    private static WidgetChromeFile SimplifiedChinese { get; } = new()
    {
        AppStatusOpen = "已开启",
        AppStatusClosed = "未开启",
        ServerLabel = "接收服务",
        ServerOn = "开",
        ServerOff = "关",
        HintAppClosed = "打开应用后才能发现设备和接收文件。",
        HintServerOn = "附近设备可以发送文件。",
        HintServerOff = "点击开启接收。",
        EmptyNoDevices = "附近没有设备",
        EmptyReceivingOff = "接收已停止",
        EmptyAppClosed = "应用未开启",
        HistoryEmpty = "无历史记录",
        NearbyTab = "附近",
        HistoryTab = "历史",
        NearbyCount = "附近 {count} 台",
        HistoryCount = "历史 {count} 条",
        FromPeer = "来自 {peer}",
        ToPeer = "发送到 {peer}",
    };
}
