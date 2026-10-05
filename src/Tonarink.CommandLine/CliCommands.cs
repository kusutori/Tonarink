using System.Diagnostics;
using LocalSendDotNet;

namespace Tonarink.Cli;

sealed partial class CliCommands(ICliRuntime runtime)
{
    private Task<CliState> StateAsync(CancellationToken token) => runtime.ReadAsync(token);

    public async Task<CliResponse> ExecuteAsync(CliRequest request, CliArguments args,
        Action<CliResponse> emit, CancellationToken token)
    {
        switch (args.Command)
        {
            case "status":
                var state = await StateAsync(token).ConfigureAwait(false);
                var id = state.Identity;
                var status = new CliStatus(true, state.NodeState.ToString(), id?.Alias, id?.Fingerprint,
                    id?.Port, id?.Protocol.ToString(), state.Error, state.DiscoveryWarning);
                return CliProtocol.Result($"{status.State}\t{status.Name}\t{status.Port}\t{status.Protocol}\n{status.Error ?? status.DiscoveryWarning}".TrimEnd(), status, CliJsonContext.Default.CliStatus);
            case "devices":
            case "discover":
                var node = await RunningNodeAsync(token).ConfigureAwait(false);
                if (args.Command == "discover") await node.RefreshAsync(token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(args.Integer("seconds", args.Command == "discover" ? 5 : 1)), token).ConfigureAwait(false);
                var devices = node.GetDevices().Select(Device).ToArray();
                return CliProtocol.Result(string.Join('\n', devices.Select(d => $"{d.Name}\t{d.Fingerprint}\t{string.Join(", ", d.Addresses)}")), devices, CliJsonContext.Default.CliDeviceArray);
            case "server": return await runtime.ServerAsync(args.Action, token).ConfigureAwait(false);
            case "send": return await SendAsync(request, args, emit, token).ConfigureAwait(false);
            case "receive": return await ReceiveAsync(request, args, emit, token).ConfigureAwait(false);
            case "cancel":
                var transferId = Guid.Parse(args.Positionals[0]);
                var pendingRequest = (await StateAsync(token).ConfigureAwait(false)).IncomingTransfers.FirstOrDefault(r => r.TransferId == transferId);
                var cancelled = await (await RunningNodeAsync(token).ConfigureAwait(false)).CancelTransferAsync(transferId, token).ConfigureAwait(false);
                if (cancelled && pendingRequest is not null) await runtime.DismissIncomingAsync(pendingRequest.RequestId, token).ConfigureAwait(false);
                return cancelled ? CliProtocol.Success("Transfer cancelled") : CliProtocol.Error("Transfer not found", 3);
            case "settings": return await runtime.SettingsAsync(request, args, token).ConfigureAwait(false);
            case "favorites": return await FavoritesAsync(args, token).ConfigureAwait(false);
            case "history": return await HistoryAsync(args, token).ConfigureAwait(false);
            case "app":
                if (args.Action == "open") await runtime.OpenAppAsync(token).ConfigureAwait(false);
                return CliProtocol.Success(args.Action == "open" ? "Main window opened" : "Host is exiting");
            case "web": return await WebAsync(request, args, token).ConfigureAwait(false);
            default: throw new CliException("Unsupported command.");
        }
    }

    private async Task<LocalSendNode> RunningNodeAsync(CancellationToken token)
    {
        var current = await StateAsync(token).ConfigureAwait(false);
        if (!current.IsServerDesired)
        {
            await runtime.ServerAsync("start", token).ConfigureAwait(false);
            current = await StateAsync(token).ConfigureAwait(false);
        }
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(45))
        {
            if (current.Node is { State: LocalSendNodeState.Running } node) return node;
            if (current.NodeState == LocalSendNodeState.Faulted) throw new CliException(current.Error ?? "Server startup failed", 1);
            await Task.Delay(100, token).ConfigureAwait(false);
            current = await StateAsync(token).ConfigureAwait(false);
        }
        throw new CliException("The server did not become ready in time.", 3);
    }

    private async Task<CliResponse> FavoritesAsync(CliArguments args, CancellationToken token)
    {
        if (args.Action == "add")
        {
            var device = await ResolveAsync(await RunningNodeAsync(token).ConfigureAwait(false), args.Positionals[0], token).ConfigureAwait(false);
            await runtime.SaveFavoriteAsync(device, token).ConfigureAwait(false);
            return CliProtocol.Success("Favorite saved: " + device.Alias);
        }
        if (args.Action == "remove")
        {
            await runtime.RemoveFavoriteAsync(args.Positionals[0], token).ConfigureAwait(false);
            return CliProtocol.Success("Favorite removed");
        }
        var favorites = await runtime.FavoritesAsync(token).ConfigureAwait(false);
        return CliProtocol.Result(string.Join('\n', favorites.Select(f => $"{f.Name}\t{f.Fingerprint}\t{f.Address}:{f.Port}")), favorites, CliJsonContext.Default.CliFavoriteArray);
    }

    private async Task<CliResponse> HistoryAsync(CliArguments args, CancellationToken token)
    {
        if (args.Action != "list")
        {
            await runtime.RemoveHistoryAsync(args.Action == "clear" ? null : Guid.Parse(args.Positionals[0]), token).ConfigureAwait(false);
            return CliProtocol.Success("History updated; saved files were not deleted");
        }
        var entries = await runtime.HistoryAsync(token).ConfigureAwait(false);
        return CliProtocol.Result(string.Join('\n', entries.Select(e => $"{e.Id}\t{e.FileName}\t{e.Path}\t{e.Sender}")), entries, CliJsonContext.Default.CliHistoryArray);
    }

    private async Task<CliResponse> WebAsync(CliRequest request, CliArguments args, CancellationToken token)
    {
        var node = await RunningNodeAsync(token).ConfigureAwait(false);
        var settings = (await StateAsync(token).ConfigureAwait(false)).Settings;
        switch (args.Action)
        {
            case "send":
                await node.StartWebShareAsync(BuildItems(request, args), new WebShareOptions
                { AutoAccept = args.Has("auto-accept"), Pin = args.Value("pin"), UiCulture = settings.Locale }, token).ConfigureAwait(false);
                break;
            case "receive":
                await node.StartWebReceiveAsync(new WebShareOptions
                { AutoAccept = args.Has("auto-accept"), Pin = args.Value("pin"), UiCulture = settings.Locale }, token).ConfigureAwait(false);
                break;
            case "stop": node.StopWebShare(); break;
            case "accept":
                if (!node.AcceptWebShareRequest(args.Positionals[0])) throw new CliException("Browser request not found.", 3);
                break;
            case "decline":
                if (!node.DeclineWebShareRequest(args.Positionals[0])) throw new CliException("Browser request not found.", 3);
                break;
        }
        var share = node.GetWebShare();
        var links = share.Active ? NetworkLinks(node.Identity!).ToArray() : [];
        var result = new CliWebShare(share.Active, share.Mode.ToString(), share.AutoAccept, share.Pin is not null,
            share.Files.Select(f => f.FileName).ToArray(), share.Requests.Where(r => r.Pending).Select(r => r.SessionId).ToArray(), links);
        return CliProtocol.Result($"Browser share: {(share.Active ? share.Mode.ToString() : "stopped")}\n" + string.Join('\n', links), result, CliJsonContext.Default.CliWebShare);
    }

    private static IEnumerable<string> NetworkLinks(LocalSendIdentity identity)
    {
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
                if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(address.Address))
                    yield return EndpointUrl(new(address.Address, identity.Port, identity.Protocol)) + "/";
        }
    }
    private static CliDevice Device(LocalSendDevice device) => new(device.Alias, device.Fingerprint,
        device.DeviceType.ToString(), device.Endpoints.Select(EndpointUrl).ToArray(), device.ProtocolVersion);
    private static string EndpointUrl(DeviceEndpoint endpoint) =>
        $"{endpoint.Protocol.ToString().ToLowerInvariant()}://{(endpoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + endpoint.Address + "]" : endpoint.Address.ToString())}:{endpoint.Port}";
}
