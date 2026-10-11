using System.Net;
using LocalSendDotNet;

namespace Tonarink.Pages.Send;

sealed record ResolvedDevice(LocalSendDevice Device);

sealed record DeviceResolutionFailure(Exception Cause);

union DeviceResolution(ResolvedDevice, DeviceResolutionFailure);

static class SendDeviceResolver
{
    public static async Task<DeviceResolution> ResolveAsync(
        LocalSendNode node,
        IPAddress address,
        int port,
        LocalSendProtocol preferredProtocol,
        string? expectedFingerprint,
        CancellationToken cancellationToken = default,
        TimeSpan? probeTimeout = null)
    {
        Exception? lastError = null;
        LocalSendProtocol[] protocols =
        [
            preferredProtocol,
            preferredProtocol == LocalSendProtocol.Https
                ? LocalSendProtocol.Http
                : LocalSendProtocol.Https,
        ];
        foreach (var protocol in protocols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var probeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (probeTimeout is { } timeout) probeDeadline.CancelAfter(timeout);
            try
            {
                var endpoint = new DeviceEndpoint(address, port, protocol);
                var probe = await node.ProbeDeviceAsync(endpoint, probeDeadline.Token).ConfigureAwait(true);
                if (expectedFingerprint is not null
                    && !string.Equals(
                        probe.Device.Fingerprint,
                        expectedFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new LocalSendException(
                        "The saved address now belongs to a different device.");
                }

                var device = await node.AddKnownDeviceAsync(
                    endpoint,
                    probe.Device.Fingerprint,
                    probeDeadline.Token).ConfigureAwait(true);
                // The registry may merge in stale endpoints from discovery.
                // Send through the endpoint we just verified, not an older one
                // that the transfer's HTTPS preference happens to select.
                return new ResolvedDevice(device with { Endpoints = (DeviceEndpoint[])[endpoint] });
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested
                      && (probeDeadline.IsCancellationRequested || exception.InnerException is TimeoutException))
            {
                // HttpClient's request deadline is a failed probe, not a user
                // cancellation. The other protocol may still reach the peer.
                lastError = exception;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        return new DeviceResolutionFailure(lastError
                                           ?? new LocalSendException("No compatible device responded."));
    }

    public static bool TryParseAddress(string value, out IPAddress address, out int port)
    {
        var input = value.Trim();
        port = LocalSendOptions.DefaultPort;
        // IPAddress.TryParse accepts bracketed IPv6 endpoints but discards
        // their port. Only let it handle a bare address (brackets optional).
        if ((!input.StartsWith('[') || input.EndsWith(']'))
            && IPAddress.TryParse(input, out address!))
            return true;

        if (IPEndPoint.TryParse(input, out var endpoint)
            && endpoint.Port is >= 1 and <= ushort.MaxValue)
        {
            address = endpoint.Address;
            port = endpoint.Port;
            return true;
        }

        address = IPAddress.None;
        return false;
    }

    public static string FormatAddress(IPAddress address, int port)
    {
        var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
        return port == LocalSendOptions.DefaultPort ? host : $"{host}:{port}";
    }
}
