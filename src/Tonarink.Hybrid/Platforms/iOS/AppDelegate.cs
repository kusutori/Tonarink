using System.Text;
using System.Text.Json;
using Foundation;
using Tonarink.Application;
using UIKit;

namespace Tonarink.Hybrid;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        if (!string.Equals(url.Scheme, "tonarink", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(url.Host, "share", StringComparison.OrdinalIgnoreCase))
            return base.OpenUrl(application, url, options);
        var id = url.Path?.Trim('/');
        if (string.IsNullOrWhiteSpace(id))
            return false;
        _ = ImportShareAsync(id);
        return true;
    }

    private static async Task ImportShareAsync(string id)
    {
        try
        {
            var root = NSFileManager.DefaultManager.GetContainerUrl("group.dev.tonarink.app")?.Path;
            if (string.IsNullOrWhiteSpace(root))
                return;
            var payloadPath = Path.Combine(root, "ShareInbox", id, "payload.json");
            await using var stream = File.OpenRead(payloadPath);
            var payload = await JsonSerializer.DeserializeAsync(stream, TonarinkJsonContext.Default.IosSharePayload);
            if (payload is null)
                return;
            var items = payload.Files.Select(file => new ShareItem(Guid.NewGuid(), file.Name, file.Size, file.ContentType, file.Path,
                OpenRead: _ => ValueTask.FromResult<Stream>(File.OpenRead(file.Path)))).ToList();
            if (payload.Text is { Length: > 0 } text)
                items.Add(new ShareItem(Guid.NewGuid(), "message.txt", Encoding.UTF8.GetByteCount(text), "text/plain", TextContent: text));
            if (items.Count == 0)
                return;
            var state = IPlatformApplication.Current?.Services.GetService<TonarinkAppState>();
            state?.AddSendItems(items);
            state?.RequestNavigation("/send");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Tonarink iOS share import failed: {exception}");
        }
    }
}
