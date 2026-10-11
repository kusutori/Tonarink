using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.UI.Reactor;
using Tonarink.Cli;

namespace Tonarink.Services.Activation;

// A null route only restores the current page. Entity requests also identify
// the page to show; CLI syntax and URI parsing stay outside the navigator.
sealed record AppNavigationRequest(AppRoute? Route = null, string? Favorite = null, Guid? History = null,
    ShareTargetPayload? Payload = null)
{
    public static AppNavigationRequest ForFavorite(string fingerprint) => new(AppRoute.Send, Favorite: fingerprint);
    public static AppNavigationRequest ForHistory(Guid id) => new(AppRoute.History, History: id);
}

static class AppNavigationService
{
    private sealed record Pending(AppNavigationRequest Request, CancellationToken Token,
        CultureInfo Culture, TaskCompletionSource Completion);
    private static readonly ConcurrentQueue<Pending> Queue = new();
    private static Func<AppNavigationRequest, CancellationToken, Task>? _handler;
    private static int _draining;

    // The shell connects only once its navigation host is ready. This also
    // buffers cold-start protocol/launch activation without creating a page.
    public static Action Connect(Func<AppNavigationRequest, CancellationToken, Task> handler)
    {
        Volatile.Write(ref _handler, handler);
        ScheduleDrain();
        return () => Interlocked.CompareExchange(ref _handler, null, handler);
    }

    public static async Task OpenAsync(AppNavigationRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Enqueue(new(request, token, CultureInfo.CurrentUICulture, completion));
        ScheduleDrain();
        await completion.Task.WaitAsync(token).ConfigureAwait(false);
    }

    public static void Open(AppNavigationRequest request) => _ = OpenAndReportAsync(request);

    private static async Task OpenAndReportAsync(AppNavigationRequest request)
    {
        try { await OpenAsync(request).ConfigureAwait(false); }
        catch (Exception exception) { AppDiagnostics.Report("Could not navigate to the requested app page", exception); }
    }

    internal static Task OnUIAsync(Func<Task> action, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var culture = CultureInfo.CurrentUICulture;
        if (ReactorApp.UIDispatcher is not { } dispatcher || !dispatcher.TryEnqueue(async () =>
        {
            try
            {
                using var language = CliText.UseCulture(culture);
                token.ThrowIfCancellationRequested();
                await action();
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
            completion.TrySetException(new CliException(CliText.Get("The app dispatcher is unavailable."), 3));
        return completion.Task;
    }

    private static void ScheduleDrain()
    {
        if (Volatile.Read(ref _handler) is null || Queue.IsEmpty
            || Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
            return;
        _ = DrainAsync();
    }

    private static async Task DrainAsync()
    {
        try
        {
            while (Volatile.Read(ref _handler) is { } handler && Queue.TryDequeue(out var pending))
            {
                try
                {
                    using var language = CliText.UseCulture(pending.Culture);
                    await OnUIAsync(() => handler(pending.Request, pending.Token), pending.Token).ConfigureAwait(false);
                    pending.Completion.TrySetResult();
                }
                catch (OperationCanceledException) when (pending.Token.IsCancellationRequested)
                { pending.Completion.TrySetCanceled(pending.Token); }
                catch (Exception exception) { pending.Completion.TrySetException(exception); }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _draining, 0);
            ScheduleDrain();
        }
    }
}
