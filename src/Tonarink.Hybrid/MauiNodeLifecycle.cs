using Tonarink.Application;
using Microsoft.Extensions.Logging;

namespace Tonarink.Hybrid;

public sealed class MauiNodeLifecycle
{
    private readonly ITonarinkRuntime _runtime;
    private readonly TonarinkAppState _state;
    private readonly ILogger<MauiNodeLifecycle> _logger;
    private readonly MauiLocalNetworkAccess _networkAccess;
    private int _started;

    public MauiNodeLifecycle(ITonarinkRuntime runtime, TonarinkAppState state, ILogger<MauiNodeLifecycle> logger, MauiLocalNetworkAccess networkAccess)
    {
        _runtime = runtime;
        _state = state;
        _logger = logger;
        _networkAccess = networkAccess;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;
        try
        {
            await _state.InitializeAsync(cancellationToken);
            await _networkAccess.EnsureAsync(cancellationToken);
#if ANDROID
            await MauiNotificationService.EnsureBackgroundPermissionAsync(cancellationToken);
#endif
            await _runtime.StartAsync(cancellationToken);
#if ANDROID
            MauiBackgroundReceiveService.Start();
#endif
            _state.SetRuntimeStatus(running: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Interlocked.Exchange(ref _started, 0);
            _logger.LogError(exception, "Tonarink mobile node could not start");
            _state.SetRuntimeStatus(running: false, exception.GetBaseException().Message);
        }
    }
}
