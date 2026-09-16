using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LocalSendDotNet;

namespace Tonarink.Services;

static class AppNetworkAddresses
{
    public static IReadOnlyList<string> ListIpv4(AppSettings settings)
        => OrderAddresses(ListAddressCandidates(settings.NetworkWhitelist, settings.NetworkBlacklist));

    public static IReadOnlyList<string> ListWebShareIpv4(AppSettings settings)
    {
        var candidates = ListAddressCandidates(settings.NetworkWhitelist, settings.NetworkBlacklist);
        if (candidates.Count == 0)
            candidates = ListAddressCandidates(whitelist: null, blacklist: null);

        if (candidates.Count == 0)
            return [];

        var bestPriority = candidates.Min(static item => item.Priority);
        return OrderAddresses(candidates.Where(item => item.Priority == bestPriority));
    }

    private static IReadOnlyList<(string Address, int Priority)> ListAddressCandidates(
        IReadOnlyList<string>? whitelist,
        IReadOnlyList<string>? blacklist)
    {
        var addresses = new List<(string Address, int Priority)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var interfaceAddresses = nic.GetIPProperties().UnicastAddresses
                .Select(static item => item.Address)
                .Where(static address =>
                    address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(static address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (interfaceAddresses.Length == 0)
                continue;
            if (NetworkAddressPatterns.IsInterfaceIgnored(
                    interfaceAddresses,
                    whitelist,
                    blacklist))
                continue;

            var hasGateway = nic.GetIPProperties().GatewayAddresses.Any(static gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork
                && !gateway.Address.Equals(IPAddress.Any));
            addresses.AddRange(interfaceAddresses.Select(address =>
                (address, AddressPriority(IPAddress.Parse(address), hasGateway))));
        }

        return
        [
            .. addresses
                .GroupBy(static item => item.Address, StringComparer.Ordinal)
                .Select(static group => group.MinBy(static item => item.Priority))
        ];
    }

    private static IReadOnlyList<string> OrderAddresses(
        IEnumerable<(string Address, int Priority)> addresses) =>
    [
        .. addresses
            .OrderBy(static item => item.Priority)
            .ThenBy(static item => item.Address, StringComparer.Ordinal)
            .Select(static item => item.Address)
    ];

    private static int AddressPriority(IPAddress address, bool hasGateway)
    {
        var octets = address.GetAddressBytes();
        var isAutomaticPrivate = octets[0] == 169 && octets[1] == 254;
        var isPrivate = octets[0] == 10
                        || octets[0] == 172 && octets[1] is >= 16 and <= 31
                        || octets[0] == 192 && octets[1] == 168;

        switch (isPrivate)
        {
            case true when hasGateway:
                return 0;
            case true:
                return 1;
        }

        if (hasGateway && !isAutomaticPrivate)
            return 2;
        return isAutomaticPrivate ? 4 : 3;
    }
}
