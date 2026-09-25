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
        var (runtime, dispatchRuntime) = context.UseReducer<AppRuntimeState, AppRuntimeAction>(
            ReduceRuntime,
            AppRuntimeState.Initial);
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
            dispatchRuntime);

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
            dispatchRuntime(new AppRuntimeAction.Starting());
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

            dispatchRuntime(new AppRuntimeAction.Stopping());
            setServerDesired(false);
        }

        async Task RunNodeSessionAsync(int session, bool desired, CancellationToken cancellationToken)
        {
            LocalSendNode? node = null;
            try
            {
                if (desired)
                    dispatchRuntime(new AppRuntimeAction.Starting());

                node = await lifecycle.StartSessionAsync(
                    session,
                    desired,
                    settings,
                    httpsOverride,
                    cancellationToken).ConfigureAwait(false);
                if (node is null)
                {
                    dispatchRuntime(new AppRuntimeAction.Stopped());
                    return;
                }

                dispatchRuntime(new AppRuntimeAction.Started(
                    node.State,
                    node.Identity,
                    node.GetDevices(),
                    settings.ResolvedMulticastAddress.ToString(),
                    settings.ResolvedReceivePin,
                    node.DiscoveryError,
                    settings.NetworkWhitelist,
                    settings.NetworkBlacklist));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when the node session is replaced or the app shuts down.
                return;
            }
            catch (Exception exception)
            {
                dispatchRuntime(new AppRuntimeAction.Failed(
                    node?.State ?? lifecycle.CurrentNode?.State ?? LocalSendNodeState.Faulted,
                    exception.Message));
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
                dispatchRuntime(new AppRuntimeAction.DevicesChanged(node.GetDevices(), change));
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
                dispatchRuntime(new AppRuntimeAction.Refreshed(node.GetDevices(), node.DiscoveryError));
            }
            catch (Exception exception)
            {
                dispatchRuntime(new AppRuntimeAction.ErrorReported(exception.Message));
            }
        }
    }

    private static AppRuntimeState ReduceRuntime(AppRuntimeState state, AppRuntimeAction action) => action switch
    {
        AppRuntimeAction.Starting => state with
        {
            NodeState = LocalSendNodeState.Starting,
            Devices = [],
            IncomingTransfers = [],
            Error = null,
            DiscoveryWarning = null,
        },
        AppRuntimeAction.Stopping => state with
        {
            NodeState = LocalSendNodeState.Stopping,
            Devices = [],
            IncomingTransfers = [],
            Error = null,
            DiscoveryWarning = null,
        },
        AppRuntimeAction.Stopped => state with
        {
            NodeState = LocalSendNodeState.Stopped,
            Devices = [],
            IncomingTransfers = [],
            Error = null,
            DiscoveryWarning = null,
        },
        AppRuntimeAction.Started started => state with
        {
            NodeState = started.NodeState,
            Identity = started.Identity,
            Devices = started.Devices,
            Error = null,
            AppliedMulticastGroup = started.AppliedMulticastGroup,
            AppliedReceivePin = started.AppliedReceivePin,
            DiscoveryWarning = started.DiscoveryWarning,
            AppliedNetworkWhitelist = started.AppliedNetworkWhitelist,
            AppliedNetworkBlacklist = started.AppliedNetworkBlacklist,
        },
        AppRuntimeAction.DevicesChanged changed => state with
        {
            Devices = changed.Devices,
            DeviceActivity = AppendDeviceActivity(state.DeviceActivity, changed.Change),
        },
        AppRuntimeAction.Refreshed refreshed => state with
        {
            Devices = refreshed.Devices,
            Error = null,
            DiscoveryWarning = refreshed.DiscoveryWarning,
        },
        AppRuntimeAction.IncomingAdded added => state with
        {
            IncomingTransfers = (IncomingTransferRequest[])[.. state.IncomingTransfers, added.Request],
        },
        AppRuntimeAction.IncomingDismissed dismissed => state with
        {
            IncomingTransfers = (IncomingTransferRequest[])
            [
                .. state.IncomingTransfers.Where(request => request.RequestId != dismissed.RequestId)
            ],
        },
        AppRuntimeAction.ErrorReported reported => state with { Error = reported.Message },
        AppRuntimeAction.Failed failed => state with
        {
            NodeState = failed.NodeState,
            Error = failed.Message,
            DiscoveryWarning = null,
        },
    };

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
