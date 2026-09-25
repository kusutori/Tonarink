using LocalSendDotNet;
using CoreLocalSendNode = LocalSendDotNet.LocalSendNode;

namespace Tonarink.Hooks.LocalSendNode;

sealed class LocalSendNodeLifecycle
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _nextSession;
    private int _ownerSession;

    public CoreLocalSendNode? CurrentNode { get; private set; }

    public int CreateSession() => Interlocked.Increment(ref _nextSession);

    public async Task<CoreLocalSendNode?> StartSessionAsync(
        int session,
        bool desired,
        AppSettings settings,
        bool? httpsOverride,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeCurrentNodeCoreAsync().ConfigureAwait(false);
            if (!desired || cancellationToken.IsCancellationRequested)
                return null;

            var node = new CoreLocalSendNode(CreateOptions(settings, httpsOverride), AppDiagnostics.LoggerFactory);
            CurrentNode = node;
            _ownerSession = session;
            await node.StartAsync(cancellationToken).ConfigureAwait(false);
            return node;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisposeSessionAsync(int session)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_ownerSession == session)
                await DisposeCurrentNodeCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static LocalSendOptions CreateOptions(AppSettings settings, bool? httpsOverride) => new()
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
    };

    private async Task DisposeCurrentNodeCoreAsync()
    {
        _ownerSession = 0;
        if (CurrentNode is not { } node)
            return;

        CurrentNode = null;
        try
        {
            await node.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Report("Could not dispose the LocalSend node", exception);
        }
    }
}
