using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Tonarink.Application;

namespace Tonarink.Hybrid;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
[IntentFilter([Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleShareIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleShareIntent(intent);
    }

    private void HandleShareIntent(Intent? intent)
    {
        if (intent?.Action is not (Intent.ActionSend or Intent.ActionSendMultiple))
            return;
        _ = ImportShareAsync(intent);
    }

    private async Task ImportShareAsync(Intent intent)
    {
        try
        {
            var state = IPlatformApplication.Current?.Services.GetService<TonarinkAppState>();
            if (state is null)
                return;
            var items = new List<ShareItem>();
            if (intent.GetStringExtra(Intent.ExtraText) is { Length: > 0 } text)
                items.Add(new ShareItem(Guid.NewGuid(), "message.txt", System.Text.Encoding.UTF8.GetByteCount(text), "text/plain", TextContent: text));

            foreach (var uri in GetSharedUris(intent))
            {
                var item = await CacheSharedFileAsync(uri);
                if (item is not null)
                    items.Add(item);
            }
            if (items.Count == 0)
                return;
            state.AddSendItems(items);
            state.RequestNavigation("/send");
        }
        catch (Exception exception)
        {
            Android.Util.Log.Error("Tonarink", exception.ToString());
        }
    }

    private IEnumerable<Android.Net.Uri> GetSharedUris(Intent intent)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (intent.ClipData is { } clipData)
        {
            for (var index = 0; index < clipData.ItemCount; index++)
            {
                var uri = clipData.GetItemAt(index)?.Uri;
                if (uri is not null && TryRemember(uri, seen))
                    yield return uri;
            }
        }

#pragma warning disable CA1422, CS0618
        if (intent.GetParcelableExtra(Intent.ExtraStream) is Android.Net.Uri single && TryRemember(single, seen))
            yield return single;
        if (intent.GetParcelableArrayListExtra(Intent.ExtraStream) is { } multiple)
            foreach (var value in multiple)
                if (value is Android.Net.Uri uri && TryRemember(uri, seen))
                    yield return uri;
#pragma warning restore CA1422, CS0618
    }

    private static bool TryRemember(Android.Net.Uri uri, HashSet<string> seen) =>
        uri.ToString() is { } value && seen.Add(value);

    private async Task<ShareItem?> CacheSharedFileAsync(Android.Net.Uri uri)
    {
        var resolver = ContentResolver;
        var fileName = uri.LastPathSegment ?? "shared-file";
        using (var cursor = resolver?.Query(uri, [Android.Provider.IOpenableColumns.DisplayName], null, null, null))
            if (cursor?.MoveToFirst() == true)
                fileName = cursor.GetString(0) ?? fileName;
        fileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "shared-file";
        var directory = Path.Combine(FileSystem.CacheDirectory, "shared");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
        await using (var source = resolver?.OpenInputStream(uri))
        {
            if (source is null)
                return null;
            await using var target = File.Create(path);
            await source.CopyToAsync(target);
        }
        var size = new FileInfo(path).Length;
        var contentType = resolver?.GetType(uri) ?? "application/octet-stream";
        return new ShareItem(Guid.NewGuid(), fileName, size, contentType, path,
            OpenRead: _ => ValueTask.FromResult<Stream>(File.OpenRead(path)));
    }
}
