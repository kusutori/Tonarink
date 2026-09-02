using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging;

namespace LocalSendDotNet;

internal sealed class WebShareService(LocalSendOptions options, ILogger logger)
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, WebShareBrowserSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IPAddress, (int Count, DateTimeOffset LockedUntil)> _pinAttempts = new();
    private IReadOnlyList<(WebShareFile File, SendItem Item)> _offered = [];
    private bool _active;
    private bool _autoAccept;
    private string? _pin;
    private WebShareMode _mode;

    public event Action? Changed;

    public WebShareState Snapshot()
    {
        lock (_gate)
        {
            if (!_active)
                return WebShareState.Inactive;
            return new WebShareState(
                true,
                _offered.Select(static item => item.File).ToArray(),
                _sessions.Values
                    .OrderBy(static session => session.CreatedAt)
                    .Select(static session => session.ToRequest())
                    .ToArray(),
                _autoAccept,
                _pin)
            {
                Mode = _mode
            };
        }
    }

    public void Start(IReadOnlyList<SendItem> items, WebShareOptions? shareOptions)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("At least one item is required.", nameof(items));
        shareOptions ??= new WebShareOptions();
        lock (_gate)
        {
            CancelPendingLocked();
            _offered = items.Select(item => (new WebShareFile(
                Guid.NewGuid().ToString("N"),
                item.FileName,
                item.Length,
                item.ContentType), item)).ToArray();
            _autoAccept = shareOptions.AutoAccept;
            _pin = string.IsNullOrWhiteSpace(shareOptions.Pin) ? null : shareOptions.Pin.Trim();
            _mode = WebShareMode.Send;
            _active = true;
            _pinAttempts.Clear();
        }
        Notify();
    }

    public void StartReceive(WebShareOptions? shareOptions)
    {
        shareOptions ??= new WebShareOptions();
        lock (_gate)
        {
            CancelPendingLocked();
            _offered = [];
            _autoAccept = shareOptions.AutoAccept;
            _pin = string.IsNullOrWhiteSpace(shareOptions.Pin) ? null : shareOptions.Pin.Trim();
            _mode = WebShareMode.Receive;
            _active = true;
            _pinAttempts.Clear();
        }
        Notify();
    }

    public bool TryAuthorizeReceive(IPAddress address, string? pin, out bool autoAccept)
    {
        lock (_gate)
        {
            autoAccept = false;
            if (!_active || _mode != WebShareMode.Receive)
                return false;
            if (!PinAllowedLocked(address, pin, out var unauthorized))
            {
                if (unauthorized)
                    throw new WebSharePinException();
                throw new WebSharePinRateLimitedException();
            }
            autoAccept = _autoAccept;
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_active)
                return;
            CancelPendingLocked();
            _offered = [];
            _autoAccept = false;
            _pin = null;
            _mode = WebShareMode.Send;
            _active = false;
            _pinAttempts.Clear();
        }
        Notify();
    }

    public void SetAutoAccept(bool autoAccept)
    {
        List<WebShareBrowserSession> pending = [];
        lock (_gate)
        {
            _autoAccept = autoAccept;
            if (autoAccept)
                pending.AddRange(_sessions.Values.Where(static session => session.Pending));
        }
        foreach (var session in pending)
            Accept(session.SessionId);
        Notify();
    }

    public void SetPin(string? pin)
    {
        lock (_gate)
            _pin = string.IsNullOrWhiteSpace(pin) ? null : pin.Trim();
        Notify();
    }

    public bool Accept(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || !session.Pending)
            return false;
        session.Pending = false;
        session.Decision.TrySetResult(true);
        Notify();
        return true;
    }

    public bool Decline(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            return false;
        session.Pending = false;
        session.Decision.TrySetResult(false);
        Notify();
        return true;
    }

    public async Task<PrepareDownloadResponseDto?> PrepareAsync(
        IPAddress address,
        string? userAgent,
        string? pin,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<(WebShareFile File, SendItem Item)> offered;
        bool autoAccept;
        WebShareBrowserSession session;
        lock (_gate)
        {
            if (!_active)
                return null;
            if (!PinAllowedLocked(address, pin, out var unauthorized))
            {
                if (unauthorized)
                    throw new WebSharePinException();
                throw new WebSharePinRateLimitedException();
            }
            offered = _offered;
            autoAccept = _autoAccept;
            session = new WebShareBrowserSession(
                Guid.NewGuid().ToString("N"),
                WebShareUserAgent.Describe(userAgent),
                FormatAddress(address));
            _sessions[session.SessionId] = session;
        }
        Notify();
        if (autoAccept)
            Accept(session.SessionId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.IncomingDecisionTimeout);
        try
        {
            var accepted = await session.Decision.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (!accepted)
                return null;
            return new PrepareDownloadResponseDto
            {
                SessionId = session.SessionId,
                Files = offered.Select(static item => new PrepareDownloadFileDto
                {
                    Id = item.File.Id,
                    FileName = item.File.FileName,
                    Size = item.File.Size
                }).ToArray()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Decline(session.SessionId);
            throw new TimeoutException("The web share request timed out.");
        }
    }

    public Task<(WebShareFile File, SendItem Item)?> OpenFileAsync(
        string sessionId,
        string fileId,
        IPAddress address,
        string? pin,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_active)
                return Task.FromResult<(WebShareFile File, SendItem Item)?>(null);
            if (!PinAllowedLocked(address, pin, out var unauthorized))
            {
                if (unauthorized)
                    throw new WebSharePinException();
                throw new WebSharePinRateLimitedException();
            }
            if (!_sessions.TryGetValue(sessionId, out var session) || session.Pending)
                return Task.FromResult<(WebShareFile File, SendItem Item)?>(null);
        }

        var offered = _offered.FirstOrDefault(item => string.Equals(item.File.Id, fileId, StringComparison.Ordinal));
        if (offered.Item is null)
            return Task.FromResult<(WebShareFile File, SendItem Item)?>(null);
        logger.LogInformation("Web share is sending {File} to session {Session}", offered.File.FileName, sessionId);
        return Task.FromResult<(WebShareFile File, SendItem Item)?>(offered);
    }

    private bool PinAllowedLocked(IPAddress address, string? pin, out bool unauthorized)
    {
        unauthorized = true;
        if (_pin is null)
            return true;
        var now = DateTimeOffset.UtcNow;
        _pinAttempts.TryGetValue(address, out var attempts);
        if (attempts.Count >= 3 && attempts.LockedUntil > now)
        {
            unauthorized = false;
            return false;
        }
        if (attempts.Count >= 3)
            attempts = (0, DateTimeOffset.MinValue);
        if (string.Equals(pin, _pin, StringComparison.Ordinal))
        {
            _pinAttempts.TryRemove(address, out _);
            return true;
        }
        if (!string.IsNullOrEmpty(pin))
        {
            var count = attempts.Count + 1;
            _pinAttempts[address] = (count, count >= 3 ? now + options.PinLockoutDuration : DateTimeOffset.MinValue);
        }
        return false;
    }

    private void CancelPendingLocked()
    {
        foreach (var session in _sessions.Values)
            session.Decision.TrySetResult(false);
        _sessions.Clear();
    }

    private void Notify() => Changed?.Invoke();

    private static string FormatAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? address.ToString()
            : address.ToString();
    }
}

internal sealed class WebShareBrowserSession(string sessionId, string deviceInfo, string ip)
{
    public string SessionId { get; } = sessionId;
    public string DeviceInfo { get; } = deviceInfo;
    public string Ip { get; } = ip;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public bool Pending { get; set; } = true;
    public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebShareRequest ToRequest() => new(SessionId, DeviceInfo, Ip, Pending);
}

internal sealed class WebSharePinException() : Exception("The web share PIN is missing or incorrect.");
internal sealed class WebSharePinRateLimitedException() : Exception("Too many incorrect web share PIN attempts.");
