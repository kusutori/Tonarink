using System.Diagnostics;
using LocalSendDotNet;

namespace Tonarink.Cli;

sealed partial class CliCommands(ICliRuntime runtime)
{
    private Task<CliState> StateAsync(CancellationToken token) => runtime.ReadAsync(token);
    public async Task<CliResponse> StatusAsync(CancellationToken token)
    {
        var state = await StateAsync(token).ConfigureAwait(false);
        var id = state.Identity;
        var status = new CliStatus(true, state.NodeState.ToString(), id?.Alias, id?.Fingerprint,
            id?.Port, id?.Protocol.ToString(), state.Error, state.DiscoveryWarning);
        return CliProtocol.Result($"{status.State}\t{status.Name}\t{status.Port}\t{status.Protocol}\n{status.Error ?? status.DiscoveryWarning}".TrimEnd(), status, CliJsonContext.Default.CliStatus);
    }
    public async Task<CliResponse> DevicesAsync(bool refresh, int seconds, CancellationToken token)
    {
        var node = await RunningNodeAsync(token).ConfigureAwait(false);
        if (refresh) await node.RefreshAsync(token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(seconds), token).ConfigureAwait(false);
        var devices = node.GetDevices().Select(Device).ToArray();
        return CliProtocol.Result(string.Join('\n', devices.Select(d => $"{d.Name}\t{d.Fingerprint}\t{string.Join(", ", d.Addresses)}")), devices, CliJsonContext.Default.CliDeviceArray);
    }
    public async Task<CliResponse> CancelAsync(Guid transferId, CancellationToken token)
    {
        var pending = (await StateAsync(token).ConfigureAwait(false)).IncomingTransfers.FirstOrDefault(r => r.TransferId == transferId);
        var cancelled = await (await RunningNodeAsync(token).ConfigureAwait(false)).CancelTransferAsync(transferId, token).ConfigureAwait(false);
        if (cancelled && pending is not null) await runtime.DismissIncomingAsync(pending.RequestId, token).ConfigureAwait(false);
        return cancelled ? CliProtocol.Success("Transfer cancelled") : CliProtocol.Error("Transfer not found", 3);
    }
    private async Task<LocalSendNode> RunningNodeAsync(CancellationToken token)
    {
        var current = await StateAsync(token).ConfigureAwait(false);
        if (!current.IsServerDesired)
        {
            await runtime.ServerAsync(CliServerAction.Start, token).ConfigureAwait(false);
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
    public async Task<CliResponse> SettingsAsync(string? key, bool showSecrets, CancellationToken token)
    {
        var entries = await runtime.ReadSettingsAsync(showSecrets, token).ConfigureAwait(false);
        if (key is not null)
        {
            entries = entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length == 0) throw new CliException("Unknown settings key: " + key);
        }
        return CliProtocol.Result(string.Join('\n', entries.Select(e => $"{e.Key}\t{e.Value}")), entries, CliJsonContext.Default.CliSettingArray);
    }
    public async Task<CliResponse> AddFavoriteAsync(string target, CancellationToken token)
    {
        var device = await ResolveAsync(await RunningNodeAsync(token).ConfigureAwait(false), target, token).ConfigureAwait(false);
        await runtime.SaveFavoriteAsync(device, token).ConfigureAwait(false);
        return CliProtocol.Success("Favorite saved: " + device.Alias);
    }
    public async Task<CliResponse> RemoveFavoriteAsync(string fingerprint, CancellationToken token)
    { await runtime.RemoveFavoriteAsync(fingerprint, token).ConfigureAwait(false); return CliProtocol.Success("Favorite removed"); }
    public async Task<CliResponse> FavoritesAsync(CancellationToken token)
    {
        var favorites = await runtime.FavoritesAsync(token).ConfigureAwait(false);
        return CliProtocol.Result(string.Join('\n', favorites.Select(f => $"{f.Name}\t{f.Fingerprint}\t{f.Address}:{f.Port}")), favorites, CliJsonContext.Default.CliFavoriteArray);
    }
    public async Task<CliResponse> RemoveHistoryAsync(Guid? id, CancellationToken token)
    { await runtime.RemoveHistoryAsync(id, token).ConfigureAwait(false); return CliProtocol.Success("History updated; saved files were not deleted"); }
    public async Task<CliResponse> HistoryAsync(CancellationToken token)
    {
        var entries = await runtime.HistoryAsync(token).ConfigureAwait(false);
        return CliProtocol.Result(string.Join('\n', entries.Select(e => $"{e.Id}\t{e.FileName}\t{e.Path}\t{e.Sender}")), entries, CliJsonContext.Default.CliHistoryArray);
    }
    public async Task<CliResponse> WebSendAsync(CliRequest request, string[] paths, string? text, bool autoAccept, string? pin, CancellationToken token)
    {
        var node = await RunningNodeAsync(token).ConfigureAwait(false);
        await node.StartWebShareAsync(BuildItems(request, paths, text), await WebOptionsAsync(autoAccept, pin, token).ConfigureAwait(false), token).ConfigureAwait(false);
        return WebStatus(node);
    }
    public async Task<CliResponse> WebReceiveAsync(bool autoAccept, string? pin, CancellationToken token)
    {
        var node = await RunningNodeAsync(token).ConfigureAwait(false);
        await node.StartWebReceiveAsync(await WebOptionsAsync(autoAccept, pin, token).ConfigureAwait(false), token).ConfigureAwait(false);
        return WebStatus(node);
    }
    public async Task<CliResponse> WebStatusAsync(CancellationToken token) => WebStatus(await RunningNodeAsync(token).ConfigureAwait(false));
    public async Task<CliResponse> WebStopAsync(CancellationToken token)
    { var node = await RunningNodeAsync(token).ConfigureAwait(false); node.StopWebShare(); return WebStatus(node); }
    public async Task<CliResponse> WebDecisionAsync(string session, bool accept, CancellationToken token)
    {
        var node = await RunningNodeAsync(token).ConfigureAwait(false);
        if (!(accept ? node.AcceptWebShareRequest(session) : node.DeclineWebShareRequest(session))) throw new CliException("Browser request not found.", 3);
        return WebStatus(node);
    }
    private async Task<WebShareOptions> WebOptionsAsync(bool autoAccept, string? pin, CancellationToken token) => new()
    { AutoAccept = autoAccept, Pin = pin, UiCulture = (await StateAsync(token).ConfigureAwait(false)).Settings.Locale };
    private static CliResponse WebStatus(LocalSendNode node)
    {
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
