using LocalSendDotNet;
using Microsoft.UI.Reactor.Localization;

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

sealed partial class LocalizedAppShell
{
    private LocalSendNodeSession UseLocalSendNode(
        AppSettings settings,
        IntlAccessor t)
    {
        var (runtime, updateRuntime) = UseReducer(AppRuntimeState.Initial);
        var runtimeRef = UseRef(runtime);
        runtimeRef.Current = runtime;
        var (serverDesired, setServerDesired) = UseState(true);
        var (serverEpoch, updateServerEpoch) = UseReducer(0);
        var (httpsOverride, setHttpsOverride) = UseState<bool?>(null);
        var nodeRef = UseRef<LocalSendNode?>(null);
        var nodeLifecycleRef = UseRef<SemaphoreSlim?>(null);
        var nodeLifecycle = nodeLifecycleRef.Current ??= new SemaphoreSlim(1, 1);
        var nextNodeSession = UseRef(0);
        var ownerNodeSession = UseRef(0);

        UseEffect(() =>
        {
            var session = ++nextNodeSession.Current;
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
                        await DisposeNodeSessionAsync(session).ConfigureAwait(false);
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
            nodeRef.Current,
            serverDesired,
            RefreshAsync,
            StartOrRestart,
            Stop,
            SetHttpsOverride,
            DismissIncoming,
            HandleIncomingActivationAsync);

        void DismissIncoming(Guid requestId)
        {
            updateRuntime(current => current with
            {
                IncomingTransfers = current.IncomingTransfers
                    .Where(request => request.RequestId != requestId)
                    .ToArray(),
            });
        }

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
            await nodeLifecycle.WaitAsync().ConfigureAwait(false);
            LocalSendNode? node = null;
            try
            {
                await DisposeCurrentNodeCoreAsync().ConfigureAwait(false);
                if (!desired || cancellationToken.IsCancellationRequested)
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

                node = new LocalSendNode(new LocalSendOptions
                {
                    Alias = settings.ResolvedAlias,
                    DeviceModel = settings.ResolvedDeviceModel,
                    DeviceType = settings.DeviceType,
                    DataDirectory = AppPlatform.DataDirectory,
                    DownloadDirectory = settings.DownloadDirectory,
                    Port = settings.Port,
                    EnableHttps = httpsOverride ?? settings.EnableHttps,
                    ReceivePin = settings.ResolvedReceivePin,
                    MulticastAddress = settings.ResolvedMulticastAddress,
                    DiscoveryTimeout = TimeSpan.FromMilliseconds(Math.Max(1, settings.DiscoveryTimeoutMs)),
                    NetworkWhitelist = settings.NetworkWhitelist,
                    NetworkBlacklist = settings.NetworkBlacklist,
                });
                nodeRef.Current = node;
                ownerNodeSession.Current = session;
                updateRuntime(current => current with
                {
                    NodeState = LocalSendNodeState.Starting,
                    Devices = [],
                    IncomingTransfers = [],
                    Error = null,
                    DiscoveryWarning = null,
                });

                await node.StartAsync(cancellationToken).ConfigureAwait(false);
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
                    NodeState = node?.State ?? LocalSendNodeState.Faulted,
                    Error = exception.Message,
                    DiscoveryWarning = null,
                });
                return;
            }
            finally
            {
                nodeLifecycle.Release();
            }

            if (node is null || cancellationToken.IsCancellationRequested)
                return;

            try
            {
                await Task.WhenAll(
                    WatchDevicesAsync(node, cancellationToken),
                    WatchIncomingTransfersAsync(node, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when the node session is replaced or the app shuts down.
            }
        }

        async Task DisposeNodeSessionAsync(int session)
        {
            await nodeLifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ownerNodeSession.Current != session)
                    return;

                await DisposeCurrentNodeCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                nodeLifecycle.Release();
            }
        }

        async Task DisposeCurrentNodeCoreAsync()
        {
            ownerNodeSession.Current = 0;
            if (nodeRef.Current is not { } node)
                return;

            nodeRef.Current = null;
            try
            {
                await node.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not dispose the LocalSend node", exception);
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

        async Task WatchIncomingTransfersAsync(LocalSendNode node, CancellationToken cancellationToken)
        {
            await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken).ConfigureAwait(false))
            {
                var currentSettings = AppSettingsStore.Load();
                var autoAccept = currentSettings.AutoSave switch
                {
                    AutoSaveMode.On => true,
                    AutoSaveMode.Favorites => FavoriteDeviceStore.Contains(request.Sender.Fingerprint),
                    _ => false,
                };
                if (autoAccept)
                {
                    _ = AutoAcceptIncomingAsync(
                        node,
                        request,
                        currentSettings.DownloadDirectory,
                        currentSettings.VerifyChecksumsOnReceive,
                        cancellationToken);
                    continue;
                }

                updateRuntime(current => current with
                {
                    IncomingTransfers = [.. current.IncomingTransfers, request],
                });
                AppNotificationService.ShowIncomingRequest(
                    t.Message(new("App", "NotificationIncomingTitle"), ("device", request.Sender.Alias)),
                    TransferOverlayVisuals.IncomingSummary(t, request.Items),
                    request.RequestId,
                    t.Message(new("App", "Accept")),
                    t.Message(new("App", "Decline")));
            }
        }

        async Task AutoAcceptIncomingAsync(
            LocalSendNode node,
            IncomingTransferRequest request,
            string downloadDirectory,
            bool verifyChecksums,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await node.AcceptAsync(
                    request.RequestId,
                    new AcceptTransferOptions
                    {
                        DestinationDirectory = downloadDirectory,
                        VerifySha256 = verifyChecksums,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    var message = result.Failure?.Message ?? t.Message(new("App", "ReceiveFailed"));
                    AppNotificationService.Show(t.Message(new("App", "ReceiveFailed")), message, "receive-failed");
                    updateRuntime(current => current with { Error = message });
                    return;
                }

                if (AppSettingsStore.Load().SaveReceiveHistory)
                    ReceiveHistoryStore.Record(request.Sender.Alias, result);
                AppNotificationService.ShowTransferComplete(
                    t.Message(new("App", "NotificationReceiveCompleteTitle")),
                    request.Items.Count == 1
                        ? t.Message(new("App", "NotificationReceiveCompleteOne"), ("device", request.Sender.Alias))
                        : t.Message(
                            new("App", "NotificationReceiveCompleteMany"),
                            ("count", request.Items.Count),
                            ("device", request.Sender.Alias)),
                    "receive-complete",
                    result.Items.Select(static item => item.SavedPath ?? string.Empty),
                    AppSettingsStore.Load().NotificationDefaultAction,
                    t.Message(new("App", "NotificationOpenFile")),
                    t.Message(new("App", "NotificationShowInFolder")));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when automatic receiving is cancelled with the node session.
            }
            catch (Exception exception)
            {
                AppNotificationService.Show(
                    t.Message(new("App", "ReceiveFailed")),
                    exception.Message,
                    "receive-failed");
                updateRuntime(current => current with { Error = exception.Message });
            }
        }

        async Task<bool> HandleIncomingActivationAsync(string action, Guid requestId)
        {
            if (nodeRef.Current is not { } node
                || runtimeRef.Current.IncomingTransfers.FirstOrDefault(
                    request => request.RequestId == requestId) is not { } request)
                return false;

            DismissIncoming(requestId);
            if (action == "incoming-accept")
            {
                var currentSettings = AppSettingsStore.Load();
                await AutoAcceptIncomingAsync(
                    node,
                    request,
                    currentSettings.DownloadDirectory,
                    currentSettings.VerifyChecksumsOnReceive,
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await node.DeclineAsync(requestId).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    updateRuntime(current => current with { Error = exception.Message });
                }
            }

            return true;
        }

        async Task RefreshAsync()
        {
            var node = nodeRef.Current;
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
            ?? Array.Empty<DeviceActivityEntry>();
        updated[change.Device.Fingerprint] = existing
            .Append(new DeviceActivityEntry(change.Kind, DateTimeOffset.Now, change.Device.Endpoints))
            .TakeLast(100)
            .ToArray();
        return updated;
    }
}
