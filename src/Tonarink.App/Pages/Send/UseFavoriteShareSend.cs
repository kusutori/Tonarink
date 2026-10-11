using System.Diagnostics;
using LocalSendDotNet;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;

namespace Tonarink.Pages.Send;

static class FavoriteShareSendHooks
{
    private sealed record LiveState(LocalSendNode? Node, AppRuntimeState Runtime,
        SendTransferController Transfer, IntlAccessor Text, Action<string> SetMessage, Action<bool> SetResolving);

    public static Action<SuggestedContactSend> UseFavoriteShareSend(this RenderContext context,
        LocalSendNode? node, AppRuntimeState runtime, SendTransferController transfer,
        IntlAccessor t, Action<string> setMessage, Action<bool> setResolving)
    {
        var live = context.UseRef(new LiveState(node, runtime, transfer, t, setMessage, setResolving));
        live.Current = new(node, runtime, transfer, t, setMessage, setResolving);
        var jobs = context.UseRef(new Queue<SuggestedContactSend>());
        var draining = context.UseRef(false);
        var lifetime = context.UseRef<CancellationTokenSource?>();
        context.UseEffect(() =>
        {
            var cancellation = new CancellationTokenSource();
            lifetime.Current = cancellation;
            return () =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
                jobs.Current.Clear();
            };
        });

        return Enqueue;

        void Enqueue(SuggestedContactSend job)
        {
            jobs.Current.Enqueue(job);
            if (!draining.Current && lifetime.Current is { IsCancellationRequested: false } cancellation)
                _ = DrainAsync(cancellation.Token);
        }

        async Task DrainAsync(CancellationToken token)
        {
            draining.Current = true;
            try
            {
                while (jobs.Current.TryDequeue(out var job))
                {
                    token.ThrowIfCancellationRequested();
                    // Do not overwrite another outgoing transfer or its PIN prompt.
                    while (live.Current.Transfer.IsBusy()) await Task.Delay(100, token);
                    if (!FavoriteDeviceStore.Entries.TryGetValue(job.Fingerprint, out var favorite))
                    {
                        Message("ShareSuggestedDeviceUnavailable");
                        continue;
                    }
                    live.Current.SetResolving(true);
                    try
                    {
                        Message("ShareSuggestedDeviceConnecting", favorite.Name);
                        var startup = Stopwatch.StartNew();
                        while (live.Current.Node?.State != LocalSendNodeState.Running
                               || live.Current.Runtime.NodeState != LocalSendNodeState.Running)
                        {
                            if (startup.Elapsed >= TimeSpan.FromSeconds(30)
                                || live.Current.Runtime.NodeState is LocalSendNodeState.Faulted
                                    or LocalSendNodeState.Stopped or LocalSendNodeState.Disposed)
                                break;
                            await Task.Delay(100, token);
                        }
                        var ready = live.Current;
                        if (ready.Node?.State != LocalSendNodeState.Running
                            || ready.Runtime.NodeState != LocalSendNodeState.Running)
                        {
                            Message("ShareSuggestedServiceUnavailable");
                            continue;
                        }
                        var device = await FavoriteDeviceResolver.ResolveAsync(ready.Node, favorite,
                            ready.Runtime.Identity?.Protocol ?? LocalSendProtocol.Https, token);
                        if (device is null)
                        {
                            Message("ShareSuggestedDeviceOffline", favorite.Name);
                            continue;
                        }
                        if (!FavoriteDeviceStore.Contains(job.Fingerprint))
                        {
                            Message("ShareSuggestedDeviceUnavailable");
                            continue;
                        }
                        token.ThrowIfCancellationRequested();
                        live.Current.SetResolving(false);
                        await live.Current.Transfer.StartItemsAsync(device, null, null, job.Items);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        AppDiagnostics.Report("Could not send shared content to a favorite", exception);
                        Message("ShareSuggestedDeviceOffline", favorite.Name);
                    }
                    finally
                    {
                        if (!token.IsCancellationRequested) live.Current.SetResolving(false);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { draining.Current = false; }
        }

        void Message(string key, string? name = null) => live.Current.SetMessage(name is null
            ? live.Current.Text.Message(new("App", key))
            : live.Current.Text.Message(new("App", key), ("device", name)));
    }
}
