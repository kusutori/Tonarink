using System.Text.Json;
using Foundation;
using Social;
using Tonarink.Application;

namespace Tonarink.Hybrid.ShareExtension;

[Register("ShareViewController")]
public sealed class ShareViewController : SLComposeServiceViewController
{
    private const string AppGroup = "group.dev.tonarink.app";

    public override bool IsContentValid() => true;

    public override async void DidSelectPost()
    {
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var root = NSFileManager.DefaultManager.GetContainerUrl(AppGroup)?.Path;
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("The Tonarink App Group container is unavailable.");
            var directory = Path.Combine(root, "ShareInbox", id);
            Directory.CreateDirectory(directory);
            var files = new List<IosShareFile>();
            var texts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ContentText))
                texts.Add(ContentText);
            foreach (var input in ExtensionContext?.InputItems ?? [])
            {
                if (input is not NSExtensionItem extensionItem)
                    continue;
                foreach (var provider in extensionItem.Attachments ?? [])
                {
                    var textType = provider.HasItemConformingTo("public.plain-text")
                        ? "public.plain-text"
                        : provider.HasItemConformingTo("public.url") ? "public.url" : null;
                    if (textType is not null)
                    {
                        if (await LoadTextAsync(provider, textType) is { Length: > 0 } text)
                            texts.Add(text);
                        continue;
                    }
                    var type = provider.RegisteredTypeIdentifiers.FirstOrDefault();
                    if (type is null)
                        continue;
                    if (await CopyFileAsync(provider, type, directory) is { } file)
                        files.Add(file);
                }
            }
            var payload = new IosSharePayload(texts.Count == 0 ? null : string.Join(Environment.NewLine, texts.Distinct()), files);
            await using (var stream = File.Create(Path.Combine(directory, "payload.json")))
                await JsonSerializer.SerializeAsync(stream, payload, TonarinkJsonContext.Default.IosSharePayload);
            if (ExtensionContext is { } context)
            {
                await context.OpenUrlAsync(new NSUrl($"tonarink://share/{id}"));
                await context.CompleteRequestAsync([]);
            }
        }
        catch
        {
            ExtensionContext?.CancelRequest(new NSError(new NSString("Tonarink.ShareExtension"), 1));
        }
    }

    public override SLComposeSheetConfigurationItem[] GetConfigurationItems() => [];

    private static Task<IosShareFile?> CopyFileAsync(NSItemProvider provider, string type, string directory)
    {
        var completion = new TaskCompletionSource<IosShareFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.LoadFileRepresentation(type, (url, error) =>
        {
            try
            {
                if (error is not null || url?.Path is not { } source || !File.Exists(source))
                {
                    completion.TrySetResult(null);
                    return;
                }
                var name = Path.GetFileName(source);
                if (string.IsNullOrWhiteSpace(name))
                    name = "shared-file";
                var destination = Path.Combine(directory, $"{Guid.NewGuid():N}-{name}");
                File.Copy(source, destination);
                completion.TrySetResult(new IosShareFile(name, destination, new FileInfo(destination).Length, type));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private static Task<string?> LoadTextAsync(NSItemProvider provider, string type)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.LoadItem(type, null, (item, error) => completion.TrySetResult(error is not null ? null : item switch
        {
            NSString value => value.ToString(),
            NSUrl value => value.AbsoluteString,
            _ => item?.ToString(),
        }));
        return completion.Task;
    }
}
