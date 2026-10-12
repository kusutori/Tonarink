using Tonarink.Application;

namespace Tonarink.WidgetProvider;

internal static class WidgetChromeCatalog
{
    public static WidgetChromeFile Resolve(string? language) => AppLanguages.Match(language) switch
    {
        AppLanguages.SimplifiedChinese => SimplifiedChinese,
        AppLanguages.Japanese => Japanese,
        AppLanguages.Korean => Korean,
        AppLanguages.French => French,
        AppLanguages.Spanish => Spanish,
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

    private static WidgetChromeFile Japanese { get; } = new()
    {
        AppStatusOpen = "実行中",
        AppStatusClosed = "終了",
        ServerLabel = "受信サービス",
        ServerOn = "オン",
        ServerOff = "オフ",
        HintAppClosed = "アプリを開いてデバイスを検出し、ファイルを受信",
        HintServerOn = "受信の準備ができています",
        HintServerOff = "クリックして受信を開始",
        EmptyNoDevices = "近くにデバイスがありません",
        EmptyReceivingOff = "受信はオフです",
        EmptyAppClosed = "アプリは終了しています",
        HistoryEmpty = "受信履歴はありません",
        NearbyTab = "近くのデバイス",
        HistoryTab = "履歴",
        NearbyCount = "近くに {count} 台",
        HistoryCount = "履歴 {count} 件",
        FromPeer = "{peer} から",
        ToPeer = "{peer} へ",
    };

    private static WidgetChromeFile Korean { get; } = new()
    {
        AppStatusOpen = "실행 중",
        AppStatusClosed = "닫힘",
        ServerLabel = "수신 서비스",
        ServerOn = "켬",
        ServerOff = "끔",
        HintAppClosed = "앱을 열어 기기를 검색하고 파일 수신",
        HintServerOn = "내용을 받을 준비가 되었습니다",
        HintServerOff = "클릭하여 수신 시작",
        EmptyNoDevices = "주변 기기가 없습니다",
        EmptyReceivingOff = "수신 꺼짐",
        EmptyAppClosed = "앱이 닫혀 있습니다",
        HistoryEmpty = "수신 기록이 없습니다",
        NearbyTab = "주변 기기",
        HistoryTab = "기록",
        NearbyCount = "주변 기기 {count}대",
        HistoryCount = "기록 {count}개",
        FromPeer = "보낸 기기: {peer}",
        ToPeer = "받는 기기: {peer}",
    };

    private static WidgetChromeFile French { get; } = new()
    {
        AppStatusOpen = "En cours d’exécution",
        AppStatusClosed = "Fermée",
        ServerLabel = "Service de réception",
        ServerOn = "Activé",
        ServerOff = "Désactivé",
        HintAppClosed = "Ouvrir l’application pour détecter des appareils et recevoir des fichiers",
        HintServerOn = "Prêt à recevoir du contenu",
        HintServerOff = "Cliquer pour démarrer la réception",
        EmptyNoDevices = "Aucun appareil à proximité",
        EmptyReceivingOff = "Réception désactivée",
        EmptyAppClosed = "L’application est fermée",
        HistoryEmpty = "Aucun historique de réception",
        NearbyTab = "À proximité",
        HistoryTab = "Historique",
        NearbyCount = "À proximité : {count}",
        HistoryCount = "Historique : {count}",
        FromPeer = "De {peer}",
        ToPeer = "À {peer}",
    };

    private static WidgetChromeFile Spanish { get; } = new()
    {
        AppStatusOpen = "En ejecución",
        AppStatusClosed = "Cerrada",
        ServerLabel = "Servicio de recepción",
        ServerOn = "Activado",
        ServerOff = "Desactivado",
        HintAppClosed = "Abre la aplicación para detectar dispositivos y recibir archivos",
        HintServerOn = "Listo para recibir contenido",
        HintServerOff = "Haz clic para empezar a recibir",
        EmptyNoDevices = "No hay dispositivos cercanos",
        EmptyReceivingOff = "Recepción desactivada",
        EmptyAppClosed = "La aplicación está cerrada",
        HistoryEmpty = "No hay historial de recepción",
        NearbyTab = "Cercanos",
        HistoryTab = "Historial",
        NearbyCount = "Cercanos: {count}",
        HistoryCount = "Historial: {count}",
        FromPeer = "De {peer}",
        ToPeer = "A {peer}",
    };
}
