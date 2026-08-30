using Tonarink.Application;

namespace Tonarink.Web;

internal sealed class TonarinkNodeHostedService : IHostedService
{
    private readonly ITonarinkRuntime _runtime;
    private readonly TonarinkAppState _state;
    private readonly ILogger<TonarinkNodeHostedService> _logger;

    public TonarinkNodeHostedService(ITonarinkRuntime runtime, TonarinkAppState state, ILogger<TonarinkNodeHostedService> logger)
    {
        _runtime = runtime;
        _state = state;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _state.InitializeAsync(cancellationToken);
            await _runtime.StartAsync(cancellationToken);
            _state.SetRuntimeStatus(running: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Tonarink LocalSend node could not start");
            _state.SetRuntimeStatus(running: false, exception.GetBaseException().Message);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.StopAsync(cancellationToken);
        }
        finally
        {
            _state.SetRuntimeStatus(running: false);
        }
    }
}
