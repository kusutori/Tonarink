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
        string? expectedFingerprint)
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
            try
            {
                var endpoint = new DeviceEndpoint(address, port, protocol);
                var probe = await node.ProbeDeviceAsync(endpoint).ConfigureAwait(true);
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
                    probe.Device.Fingerprint).ConfigureAwait(true);
                return new ResolvedDevice(device);
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
        if (IPAddress.TryParse(input, out address!))
            return true;

        if (Uri.TryCreate($"tcp://{input}", UriKind.Absolute, out var uri)
            && IPAddress.TryParse(uri.Host, out address!)
            && uri.Port is >= 1 and <= ushort.MaxValue)
        {
            port = uri.Port;
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
