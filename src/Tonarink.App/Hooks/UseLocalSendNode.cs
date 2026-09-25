using LocalSendDotNet;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;

namespace Tonarink.Hooks;

sealed record LocalSendNodeSession(
    AppRuntimeState Runtime,
    LocalSendNode? Node,
    bool IsServerDesired,
    Func<Task> RefreshAsync,
    Action StartOrRestart,
    Action Stop,
    Action<bool?> SetHttpsOverride,
    Action<Guid> DismissIncoming,
    Func<string, Guid, Task<bool>> HandleIncomingActivationAsync);

static class LocalSendNodeHooks
{
    public static LocalSendNodeSession UseLocalSendNode(
        this RenderContext context,
        AppSettings settings,
        IntlAccessor t)
    {
        var (runtime, updateRuntime) = context.UseReducer(AppRuntimeState.Initial);
        var runtimeRef = context.UseRef(runtime);
        runtimeRef.Current = runtime;
        var intlRef = context.UseRef(t);
        intlRef.Current = t;
        var (serverDesired, setServerDesired) = context.UseState(true);
        var (serverEpoch, updateServerEpoch) = context.UseReducer(0);
        var (httpsOverride, setHttpsOverride) = context.UseState<bool?>(null);
        var lifecycleRef = context.UseRef<LocalSendNodeLifecycle?>();
        var lifecycle = lifecycleRef.Current ??= new LocalSendNodeLifecycle();
        var incomingRef = context.UseRef<IncomingTransferCoordinator?>();
        var incoming = incomingRef.Current ??= new(
            () => intlRef.Current,
            () => lifecycle.CurrentNode,
            () => runtimeRef.Current,
            updateRuntime);

        context.UseEffect(() =>
        {
            var session = lifecycle.CreateSession();
            var cancellation = new CancellationTokenSource();
            _ = RunNodeSessionAsync(session, serverDesired, cancellation.Token);
            return () =>
            {
                cancellation.Cancel();
                _ = CleanupNodeSessionAsync();

                async Task CleanupNodeSessionAsync()
                {
                    try
                    {
                        await lifecycle.DisposeSessionAsync(session).ConfigureAwait(false);
                    }
                    finally
                    {
                        cancellation.Dispose();
                    }
                }
            };
        }, serverDesired, serverEpoch, httpsOverride);

        return new(
            runtime,
            lifecycle.CurrentNode,
            serverDesired,
            RefreshAsync,
            StartOrRestart,
            Stop,
            SetHttpsOverride,
            incoming.Dismiss,
            incoming.HandleActivationAsync);

        void StartOrRestart()
        {
            updateRuntime(current => current with
            {
                NodeState = LocalSendNodeState.Starting,
                Devices = [],
                IncomingTransfers = [],
                Error = null,
                DiscoveryWarning = null,
            });
            setServerDesired(true);
            updateServerEpoch(epoch => epoch + 1);
        }

        void SetHttpsOverride(bool? value)
        {
            if (httpsOverride == value)
                return;

            setHttpsOverride(value);
            if (serverDesired)
                StartOrRestart();
        }

        void Stop()
        {
            if (!serverDesired && runtime.NodeState is LocalSendNodeState.Stopped
                    or LocalSendNodeState.Created or LocalSendNodeState.Disposed)
                return;

            updateRuntime(current => current with
            {
                NodeState = LocalSendNodeState.Stopping,
                Devices = [],
                IncomingTransfers = [],
                Error = null,
                DiscoveryWarning = null,
            });
            setServerDesired(false);
        }

        async Task RunNodeSessionAsync(int session, bool desired, CancellationToken cancellationToken)
        {
            LocalSendNode? node = null;
            try
            {
                if (desired)
                {
                    updateRuntime(current => current with
                    {
                        NodeState = LocalSendNodeState.Starting,
                        Devices = [],
                        IncomingTransfers = [],
                        Error = null,
                        DiscoveryWarning = null,
                    });
                }

                node = await lifecycle.StartSessionAsync(
                    session,
                    desired,
                    settings,
                    httpsOverride,
                    cancellationToken).ConfigureAwait(false);
                if (node is null)
                {
                    updateRuntime(current => current with
                    {
                        NodeState = LocalSendNodeState.Stopped,
                        Devices = [],
                        IncomingTransfers = [],
                        Error = null,
                        DiscoveryWarning = null,
                    });
                    return;
                }

                updateRuntime(current => current with
                {
                    NodeState = node.State,
                    Identity = node.Identity,
                    Devices = node.GetDevices(),
                    Error = null,
                    AppliedMulticastGroup = settings.ResolvedMulticastAddress.ToString(),
                    AppliedReceivePin = settings.ResolvedReceivePin,
                    DiscoveryWarning = node.DiscoveryError,
                    AppliedNetworkWhitelist = settings.NetworkWhitelist,
                    AppliedNetworkBlacklist = settings.NetworkBlacklist,
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when the node session is replaced or the app shuts down.
                return;
            }
            catch (Exception exception)
            {
                updateRuntime(current => current with
                {
                    NodeState = node?.State ?? lifecycle.CurrentNode?.State ?? LocalSendNodeState.Faulted,
                    Error = exception.Message,
                    DiscoveryWarning = null,
                });
                return;
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                await Task.WhenAll(
                    WatchDevicesAsync(node, cancellationToken),
                    incoming.WatchAsync(node, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when the node session is replaced or the app shuts down.
            }
        }

        async Task WatchDevicesAsync(LocalSendNode node, CancellationToken cancellationToken)
        {
            await foreach (var change in node.WatchDeviceChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                updateRuntime(current => current with
                {
                    Devices = node.GetDevices(),
                    DeviceActivity = AppendDeviceActivity(current.DeviceActivity, change),
                });
            }
        }

        async Task RefreshAsync()
        {
            var node = lifecycle.CurrentNode;
            if (node?.State != LocalSendNodeState.Running)
                return;

            try
            {
                await node.RefreshAsync().ConfigureAwait(false);
                updateRuntime(current => current with
                {
                    Devices = node.GetDevices(),
                    Error = null,
                    DiscoveryWarning = node.DiscoveryError,
                });
            }
            catch (Exception exception)
            {
                updateRuntime(current => current with { Error = exception.Message });
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DeviceActivityEntry>> AppendDeviceActivity(
        IReadOnlyDictionary<string, IReadOnlyList<DeviceActivityEntry>> activity,
        DeviceChange change)
    {
        var updated = activity.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        var existing = updated.GetValueOrDefault(change.Device.Fingerprint)
                       ?? [];
        updated[change.Device.Fingerprint] = (DeviceActivityEntry[])
        [
            .. existing
                .Append(new DeviceActivityEntry(change.Kind, DateTimeOffset.Now, change.Device.Endpoints))
                .TakeLast(100)
        ];
        return updated;
    }
}
