using System.Net;
using LocalSendDotNet;

namespace Tonarink.Services.Devices;

static class FavoriteDeviceResolver
{
    // Retry discovery/probing, not the transfer itself. Re-offering content after
    // an ambiguous upload failure can send it twice.
    public static async Task<LocalSendDevice?> ResolveAsync(LocalSendNode node, FavoriteDevice favorite,
        LocalSendProtocol preferredProtocol, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0) await Task.Delay(TimeSpan.FromMilliseconds(800 * attempt), deadline.Token);
                var tried = new HashSet<(IPAddress Address, int Port)>();
                if (await ResolveDiscoveredAsync() is { } known) return known;

                using (var refresh = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
                {
                    refresh.CancelAfter(TimeSpan.FromMilliseconds(1500));
                    try { await node.RefreshAsync(refresh.Token); }
                    catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    { AppDiagnostics.Report("Could not refresh discovery for a favorite share", exception); }
                }
                // Announcements are processed asynchronously after the refresh.
                await Task.Delay(250, deadline.Token);
                if (await ResolveDiscoveredAsync() is { } discovered) return discovered;

                if (IPAddress.TryParse(favorite.Address, out var address) && tried.Add((address, favorite.Port)))
                {
                    var resolution = await SendDeviceResolver.ResolveAsync(node, address, favorite.Port,
                        preferredProtocol, favorite.Fingerprint, deadline.Token, TimeSpan.FromMilliseconds(1500));
                    if (resolution is ResolvedDevice(var device)) return device;
                }
                if (await ResolveDiscoveredAsync() is { } updated) return updated;

                async Task<LocalSendDevice?> ResolveDiscoveredAsync()
                {
                    var candidate = node.GetDevices().FirstOrDefault(device =>
                        string.Equals(device.Fingerprint, favorite.Fingerprint, StringComparison.Ordinal));
                    if (candidate is null) return null;
                    // The device cache can contain old endpoints. Verify that a
                    // current endpoint responds before beginning the only upload.
                    foreach (var endpoint in candidate.Endpoints.OrderByDescending(endpoint =>
                                 endpoint.Protocol == LocalSendProtocol.Https))
                    {
                        if (!tried.Add((endpoint.Address, endpoint.Port))) continue;
                        var resolution = await SendDeviceResolver.ResolveAsync(node, endpoint.Address, endpoint.Port,
                            endpoint.Protocol, favorite.Fingerprint, deadline.Token, TimeSpan.FromMilliseconds(1500));
                        if (resolution is ResolvedDevice(var device)) return device;
                    }
                    return null;
                }
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }
}
