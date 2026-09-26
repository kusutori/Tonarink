using Windows.ApplicationModel.DataTransfer;

namespace Tonarink.Services.Platform;

static class ClipboardTextService
{
    private const int MaximumAttempts = 3;

    public static async Task<bool> TryCopyAsync(string text, string description)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(text);

                if (!Clipboard.SetContentWithOptions(package, new ClipboardContentOptions()))
                {
                    if (attempt < MaximumAttempts)
                        await Task.Delay(50).ConfigureAwait(true);
                    continue;
                }

                try
                {
                    Clipboard.Flush();
                }
                catch (Exception exception)
                {
                    // SetContentWithOptions has already made the text available. Flush only
                    // keeps it available after exit and can fail while another process owns
                    // the clipboard, so it must not turn a successful copy into an app crash.
                    AppDiagnostics.Report($"Could not persist {description} on the clipboard", exception);
                }

                return true;
            }
            catch (Exception exception) when (attempt < MaximumAttempts)
            {
                AppDiagnostics.Report($"Clipboard copy attempt {attempt} failed for {description}", exception);
                await Task.Delay(50).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report($"Could not copy {description}", exception);
                return false;
            }
        }

        AppDiagnostics.Write(
            Microsoft.Extensions.Logging.LogLevel.Warning,
            "clipboard",
            $"Could not copy {description}: the clipboard rejected all attempts.");
        return false;
    }
}
