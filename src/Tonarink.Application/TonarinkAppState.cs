namespace Tonarink.Application;

public sealed class TonarinkAppState
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _settingsIo = new(1, 1);
    private IReadOnlyList<NearbyDevice> _devices = [];
    private IReadOnlyList<ShareItem> _sendItems = [];
    private IReadOnlyList<IncomingOffer> _incomingOffers = [];
    private IReadOnlyList<TransferActivity> _transfers = [];
    private TonarinkSettings _settings;
    private bool _runtimeRunning;
    private string? _runtimeError;
    private string? _requestedRoute;
    private bool _settingsInitialized;

    public TonarinkAppState(IPlatformServices platform)
    {
        Platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _settings = TonarinkSettings.CreateDefault(platform.DownloadDirectory, platform.DefaultAlias);
    }

    public event Action? Changed;

    public IPlatformServices Platform { get; }

    public TonarinkSettings Settings { get { lock (_gate) return _settings; } }

    public IReadOnlyList<NearbyDevice> Devices { get { lock (_gate) return _devices; } }

    public IReadOnlyList<ShareItem> SendItems { get { lock (_gate) return _sendItems; } }

    public IReadOnlyList<IncomingOffer> IncomingOffers { get { lock (_gate) return _incomingOffers; } }

    public IReadOnlyList<TransferActivity> Transfers { get { lock (_gate) return _transfers; } }

    public bool RuntimeRunning { get { lock (_gate) return _runtimeRunning; } }

    public string? RuntimeError { get { lock (_gate) return _runtimeError; } }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _settingsIo.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_settingsInitialized)
                return;
            var stored = await Platform.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (stored is not null)
                Mutate(() => _settings = NormalizeSettings(stored));
            _settingsInitialized = true;
        }
        finally
        {
            _settingsIo.Release();
        }
    }

    public async Task UpdateSettingsAsync(Func<TonarinkSettings, TonarinkSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        TonarinkSettings updated = null!;
        Mutate(() => updated = _settings = NormalizeSettings(update(_settings)));
        await _settingsIo.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Platform.SaveSettingsAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _settingsIo.Release();
        }
    }

    public void ReplaceDevices(IEnumerable<NearbyDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        Mutate(() => _devices = devices.OrderByDescending(static item => item.LastSeen).ToArray());
    }

    public void AddSendItems(IEnumerable<ShareItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Mutate(() => _sendItems = [.. _sendItems, .. items]);
    }

    public void RemoveSendItem(Guid id) => Mutate(() => _sendItems = _sendItems.Where(item => item.Id != id).ToArray());

    public void ClearSendItems() => Mutate(() => _sendItems = []);

    public void ReplaceIncomingOffers(IEnumerable<IncomingOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        Mutate(() => _incomingOffers = offers.OrderByDescending(static item => item.ReceivedAt).ToArray());
    }

    public void UpsertTransfer(TransferActivity transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        Mutate(() => _transfers = [transfer, .. _transfers.Where(item => item.Id != transfer.Id).Take(99)]);
    }

    public void SetRuntimeStatus(bool running, string? error = null) => Mutate(() =>
    {
        _runtimeRunning = running;
        _runtimeError = error;
    });

    public void RequestNavigation(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        Mutate(() => _requestedRoute = route);
    }

    public string? TakeRequestedRoute()
    {
        lock (_gate)
        {
            var route = _requestedRoute;
            _requestedRoute = null;
            return route;
        }
    }

    private void Mutate(Action mutation)
    {
        lock (_gate)
            mutation();
        Changed?.Invoke();
    }

    private TonarinkSettings NormalizeSettings(TonarinkSettings settings) => settings with
    {
        Alias = string.IsNullOrWhiteSpace(settings.Alias) ? "Tonarink" : settings.Alias.Trim(),
        DownloadDirectory = string.IsNullOrWhiteSpace(settings.DownloadDirectory) ? Platform.DownloadDirectory : settings.DownloadDirectory,
    };
}
