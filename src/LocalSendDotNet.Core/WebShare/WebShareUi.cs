using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalSendDotNet;

internal sealed class WebShareUiStrings
{
    public required string HtmlLang { get; init; }
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
        return prefix.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChinese
            : English;
    }

    public static string ToJson(WebShareUiStrings ui) =>
        JsonSerializer.Serialize(ui, WebShareUiJsonContext.Default.WebShareUiStrings);

    private static WebShareUiStrings English { get; } = new()
    {
        HtmlLang = "en",
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
        NoneAccepted = "No files were accepted",
        UploadFailed = "Upload failed",
    };

    private static WebShareUiStrings SimplifiedChinese { get; } = new()
    {
        HtmlLang = "zh-CN",
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
        NoneAccepted = "没有文件被接受",
        UploadFailed = "上传失败",
    };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebShareUiStrings))]
internal sealed partial class WebShareUiJsonContext : JsonSerializerContext;
