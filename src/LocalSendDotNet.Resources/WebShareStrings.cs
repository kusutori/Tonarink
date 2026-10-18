using System.Text;
using System.Text.Json;

namespace LocalSendDotNet.Resources;

/// <summary>Browser UI text shared without reflection-based JSON serialization.</summary>
public sealed class WebShareStrings
{
    public required string HtmlLang { get; init; }
    public required string PinPrompt { get; init; }
    public required string Pick { get; init; }
    public required string Waiting { get; init; }
    public required string IncorrectPin { get; init; }
    public required string RateLimited { get; init; }
    public required string Busy { get; init; }
    public required string Declined { get; init; }
    public required string TimedOut { get; init; }
    public required string TooLarge { get; init; }
    public required string RequestFailed { get; init; }
    public required string Uploading { get; init; }
    public required string SentFiles { get; init; }
    public required string SentFile { get; init; }
    public required string NoneAccepted { get; init; }
    public required string UploadFailed { get; init; }

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("htmlLang", HtmlLang);
        writer.WriteString("pinPrompt", PinPrompt);
        writer.WriteString("pick", Pick);
        writer.WriteString("waiting", Waiting);
        writer.WriteString("incorrectPin", IncorrectPin);
        writer.WriteString("rateLimited", RateLimited);
        writer.WriteString("busy", Busy);
        writer.WriteString("declined", Declined);
        writer.WriteString("timedOut", TimedOut);
        writer.WriteString("tooLarge", TooLarge);
        writer.WriteString("requestFailed", RequestFailed);
        writer.WriteString("uploading", Uploading);
        writer.WriteString("sentFiles", SentFiles);
        writer.WriteString("sentFile", SentFile);
        writer.WriteString("noneAccepted", NoneAccepted);
        writer.WriteString("uploadFailed", UploadFailed);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
