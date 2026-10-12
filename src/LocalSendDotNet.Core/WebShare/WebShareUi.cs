using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalSendDotNet;

internal sealed class WebShareUiStrings
{
    public required string HtmlLang { get; init; }
    public required string PinPrompt { get; init; }
    public required string Pick { get; init; }
    public required string Waiting { get; init; }
    public required string IncorrectPin { get; init; }
    public required string TooManyPinAttempts { get; init; }
    public required string Declined { get; init; }
    public required string TimedOut { get; init; }
    public required string TooLarge { get; init; }
    public required string RequestFailed { get; init; }
    public required string Uploading { get; init; }
    public required string SentFiles { get; init; }
    public required string SentFile { get; init; }
    public required string NoneAccepted { get; init; }
    public required string UploadFailed { get; init; }
}

internal static class WebShareUi
{
    public static WebShareUiStrings Resolve(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return English;

        var dash = culture.IndexOf('-');
        var prefix = dash < 0 ? culture : culture[..dash];
        return prefix.ToLowerInvariant() switch
        {
            "zh" => SimplifiedChinese,
            "ja" => Japanese,
            "ko" => Korean,
            "fr" => French,
            "es" => Spanish,
            _ => English,
        };
    }

    public static string ToJson(WebShareUiStrings ui) =>
        JsonSerializer.Serialize(ui, WebShareUiJsonContext.Default.WebShareUiStrings);

    private static WebShareUiStrings English { get; } = new()
    {
        HtmlLang = "en",
        PinPrompt = "Enter PIN",
        Pick = "Choose files to upload",
        Waiting = "Waiting for Tonarink…",
        IncorrectPin = "Incorrect PIN",
        TooManyPinAttempts = "Too many PIN attempts",
        Declined = "The receiver declined",
        TimedOut = "The request timed out",
        TooLarge = "The selection is too large",
        RequestFailed = "Request failed: ",
        Uploading = "Uploading",
        SentFiles = "Sent {count} files",
        SentFile = "Sent {count} file",
        NoneAccepted = "No files were accepted",
        UploadFailed = "Upload failed",
    };

    private static WebShareUiStrings SimplifiedChinese { get; } = new()
    {
        HtmlLang = "zh-CN",
        PinPrompt = "请输入 PIN",
        Pick = "选择并上传文件",
        Waiting = "等待 Tonarink 接收…",
        IncorrectPin = "PIN 不正确",
        TooManyPinAttempts = "PIN 尝试次数过多",
        Declined = "接收方已拒绝",
        TimedOut = "等待接收超时",
        TooLarge = "文件过大或数量过多",
        RequestFailed = "请求失败：",
        Uploading = "正在上传",
        SentFiles = "已发送 {count} 个文件",
        SentFile = "已发送 {count} 个文件",
        NoneAccepted = "没有文件被接受",
        UploadFailed = "上传失败",
    };

    private static WebShareUiStrings Japanese { get; } = new()
    {
        HtmlLang = "ja",
        PinPrompt = "PIN を入力してください",
        Pick = "アップロードするファイルを選択",
        Waiting = "Tonarink の応答を待っています…",
        IncorrectPin = "PIN が違います",
        TooManyPinAttempts = "PIN の試行回数が多すぎます",
        Declined = "受信側が拒否しました",
        TimedOut = "リクエストがタイムアウトしました",
        TooLarge = "選択したファイルが大きすぎるか、多すぎます",
        RequestFailed = "リクエストに失敗しました: ",
        Uploading = "アップロード中",
        SentFiles = "{count} 件のファイルを送信しました",
        SentFile = "{count} 件のファイルを送信しました",
        NoneAccepted = "承認されたファイルはありません",
        UploadFailed = "アップロードに失敗しました",
    };

    private static WebShareUiStrings Korean { get; } = new()
    {
        HtmlLang = "ko",
        PinPrompt = "PIN을 입력하세요",
        Pick = "업로드할 파일 선택",
        Waiting = "Tonarink 응답 대기 중…",
        IncorrectPin = "PIN이 올바르지 않습니다",
        TooManyPinAttempts = "PIN 입력 횟수가 너무 많습니다",
        Declined = "수신자가 거절했습니다",
        TimedOut = "요청 시간이 초과되었습니다",
        TooLarge = "선택한 파일이 너무 크거나 많습니다",
        RequestFailed = "요청 실패: ",
        Uploading = "업로드 중",
        SentFiles = "파일 {count}개를 보냈습니다",
        SentFile = "파일 {count}개를 보냈습니다",
        NoneAccepted = "수락된 파일이 없습니다",
        UploadFailed = "업로드 실패",
    };

    private static WebShareUiStrings French { get; } = new()
    {
        HtmlLang = "fr",
        PinPrompt = "Saisir le PIN",
        Pick = "Choisir les fichiers à envoyer",
        Waiting = "En attente de Tonarink…",
        IncorrectPin = "PIN incorrect",
        TooManyPinAttempts = "Trop de tentatives de PIN",
        Declined = "Le destinataire a refusé",
        TimedOut = "Le délai de la demande est dépassé",
        TooLarge = "La sélection est trop volumineuse",
        RequestFailed = "Échec de la demande : ",
        Uploading = "Envoi en cours",
        SentFiles = "{count} fichiers envoyés",
        SentFile = "{count} fichier envoyé",
        NoneAccepted = "Aucun fichier n’a été accepté",
        UploadFailed = "Échec de l’envoi",
    };

    private static WebShareUiStrings Spanish { get; } = new()
    {
        HtmlLang = "es",
        PinPrompt = "Introduce el PIN",
        Pick = "Elegir archivos para subir",
        Waiting = "Esperando a Tonarink…",
        IncorrectPin = "PIN incorrecto",
        TooManyPinAttempts = "Demasiados intentos de PIN",
        Declined = "El destinatario rechazó la solicitud",
        TimedOut = "Se agotó el tiempo de la solicitud",
        TooLarge = "La selección es demasiado grande",
        RequestFailed = "Error de solicitud: ",
        Uploading = "Subiendo",
        SentFiles = "{count} archivos enviados",
        SentFile = "{count} archivo enviado",
        NoneAccepted = "No se aceptó ningún archivo",
        UploadFailed = "Error de subida",
    };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebShareUiStrings))]
internal sealed partial class WebShareUiJsonContext : JsonSerializerContext;
