using System.Net;

namespace Tonarink.Application;

public static class DeviceAddress
{
    public const int DefaultPort = 53317;

    public static bool TryParse(string? value, out IPAddress address, out int port)
    {
        address = IPAddress.None;
        port = DefaultPort;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var input = value.Trim();
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
        port = DefaultPort;
        return false;
    }

    public static string Format(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
        return port == DefaultPort ? host : $"{host}:{port}";
    }
}
