using System.Diagnostics;
using System.Net;
using LocalSendDotNet;
using Tonarink.Application;

namespace Tonarink.Cli;

sealed partial class CliCommands
{
    public async Task<CliResponse> SendAsync(CliRequest request, string[] paths, string? text, string targetName,
        string? pin, bool noChecksum, Action<CliResponse> emit, CancellationToken token)
    {
        var items = BuildItems(request, paths, text);
        var node = await RunningNodeAsync(token).ConfigureAwait(false);
        var target = await ResolveAsync(node, targetName, token).ConfigureAwait(false);
        var settings = (await StateAsync(token).ConfigureAwait(false)).Settings;
        var outcome = await node.SendAsync(target, items, new SendOptions
        {
            Pin = pin,
            ComputeSha256 = !noChecksum && settings.VerifyChecksumsOnSend,
        }, new CliTransferProgress(emit), token).ConfigureAwait(false);
        return outcome switch
        {
            SendOutcome.Completed complete => TransferResult(complete.TransferId, "Completed", complete.Items, 0),
            SendOutcome.Cancelled cancelled => TransferResult(cancelled.TransferId, "Cancelled", cancelled.Items, 130),
            SendOutcome.PinRequired required => CliProtocol.Error(required.InvalidPin ? "The PIN is incorrect" : "The receiving device requires --pin PIN", 5),
            SendOutcome.PinRateLimited => CliProtocol.Error("Too many PIN attempts; try again later", 5),
            SendOutcome.PeerBusy => CliProtocol.Error("The receiving device is busy", 4),
            SendOutcome.Declined => CliProtocol.Error("The receiving device declined the transfer", 4),
            SendOutcome.Failed failed => TransferResult(failed.TransferId, "Failed", failed.Items, 1, failed.Failure.Message),
        };
    }

    private async Task<LocalSendDevice> ResolveAsync(LocalSendNode node, string target, CancellationToken token)
    {
        var matches = node.GetDevices().Where(d => d.Alias.Equals(target, StringComparison.OrdinalIgnoreCase)
            || d.Fingerprint.Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new CliException("Ambiguous device name; use a fingerprint or address.");
        if (matches.Length == 1) return matches[0];
        var favorites = (await runtime.FavoritesAsync(token).ConfigureAwait(false)).Where(f => f.Name.Equals(target, StringComparison.OrdinalIgnoreCase)
            || f.Fingerprint.Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (favorites.Length > 1) throw new CliException("Ambiguous favorite name; use a fingerprint.");
        if (favorites.Length == 1)
        {
            var favorite = favorites[0];
            var endpoint = new DeviceEndpoint(IPAddress.Parse(favorite.Address), favorite.Port, LocalSendProtocol.Https);
            DeviceProbeResult probe;
            try { probe = await node.ProbeDeviceAsync(endpoint, token).ConfigureAwait(false); }
            catch (HttpRequestException)
            {
                // Favorites predate CLI support and do not store the protocol.
                // Always verify the saved fingerprint, including on HTTP fallback.
                endpoint = new(endpoint.Address, endpoint.Port, LocalSendProtocol.Http);
                probe = await node.ProbeDeviceAsync(endpoint, token).ConfigureAwait(false);
            }
            if (!probe.Device.Fingerprint.Equals(favorite.Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new CliException("The saved address belongs to a different device.", 1);
            return await node.AddKnownDeviceAsync(endpoint, favorite.Fingerprint, token).ConfigureAwait(false);
        }
        DeviceEndpoint? manual = null;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var uriAddress))
        {
            if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
                throw new CliException("Use an HTTP(S) IP address and port, without paths, credentials or query parameters.");
            var authority = target[(target.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/', 2)[0];
            var explicitPort = authority.StartsWith('[') ? authority.Contains("]:", StringComparison.Ordinal) : authority.Contains(':');
            manual = new(uriAddress, explicitPort ? uri.Port : LocalSendOptions.DefaultPort,
                uri.Scheme == "https" ? LocalSendProtocol.Https : LocalSendProtocol.Http);
        }
        else if (DeviceAddress.TryParse(target, out var address, out var port))
            manual = new(address, port, LocalSendProtocol.Https);
        if (manual is not null)
        {
            var probe = await node.ProbeDeviceAsync(manual, token).ConfigureAwait(false);
            return await node.AddKnownDeviceAsync(manual, probe.Device.Fingerprint, token).ConfigureAwait(false);
        }
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(200, token).ConfigureAwait(false);
            matches = node.GetDevices().Where(d => d.Alias.Equals(target, StringComparison.OrdinalIgnoreCase)
                || d.Fingerprint.Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) throw new CliException("Ambiguous device name; use a fingerprint or address.");
            if (matches.Length == 1) return matches[0];
        }
        throw new CliException($"Device '{target}' was not found. Use tonarink discover or provide an IP address.", 3);
    }

    private static List<SendItem> BuildItems(CliRequest request, string[] paths, string? text)
    {
        if (text is not null)
            return [new SendTextItem(text == "-" ? request.StandardInput ?? throw new CliException("No stdin text was supplied.") : text)];
        var items = new List<SendItem>();
        foreach (var input in paths)
        {
            var path = CliPath.FullPath(input, request.WorkingDirectory);
            if (File.Exists(path)) items.Add(new SendFileItem(path));
            else if (Directory.Exists(path))
            {
                var prefix = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                items.AddRange(LocalSendItems.FromDirectory(path).Select(file => new SendFileItem(file.Path,
                    prefix.Length == 0 ? file.FileName : prefix + "/" + file.FileName)));
            }
            else throw new CliException("File or folder does not exist: " + input);
        }
        if (items.Count == 0) throw new CliException("No files to send.");
        if (items.Count > 10000) throw new CliException("At most 10000 files may be sent in one transfer.");
        if (items.Select(i => i.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new CliException("Duplicate destination filenames; send the files separately or rename them.");
        return items;
    }

    public async Task<CliResponse> WatchAsync(CliRequest request, int? seconds, bool autoAccept,
        string? directory, bool noChecksum, Action<CliResponse> emit, CancellationToken token)
    {
        _ = await RunningNodeAsync(token).ConfigureAwait(false);
        using (runtime.BeginReceiveWatch())
        {
            var seen = new HashSet<Guid>();
            var clock = Stopwatch.StartNew();
            while (seconds is null || clock.Elapsed < TimeSpan.FromSeconds(seconds.Value))
            {
                var state = await StateAsync(token).ConfigureAwait(false);
                foreach (var incoming in state.IncomingTransfers)
                {
                    if (!seen.Add(incoming.RequestId)) continue;
                    var value = Incoming(incoming);
                    emit(CliProtocol.Result($"Incoming {value.Id}\t{value.Sender}\t{string.Join(", ", value.Files)}",
                        new[] { value }, CliJsonContext.Default.CliIncomingArray) with
                    { Type = "incoming" });
                    if (autoAccept)
                    {
                        var result = await AcceptAsync(request, incoming.RequestId, directory, noChecksum, emit, token).ConfigureAwait(false);
                        emit(result with { Type = "transfer" });
                    }
                }
                await Task.Delay(200, token).ConfigureAwait(false);
            }
            return CliProtocol.Success("Receive watch finished");
        }
    }
    public async Task<CliResponse> DeclineAsync(Guid id, CancellationToken token)
    {
        var state = await StateAsync(token).ConfigureAwait(false);
        if (!state.IncomingTransfers.Any(r => r.RequestId == id)) throw new CliException("Incoming request not found.", 3);
        var node = state.Node ?? throw new CliException("Server is stopped.", 3);
        await node.DeclineAsync(id, token).ConfigureAwait(false);
        await runtime.DismissIncomingAsync(id, token).ConfigureAwait(false);
        return CliProtocol.Success("Incoming request declined");
    }
    public async Task<CliResponse> IncomingAsync(CancellationToken token)
    {
        var pending = (await StateAsync(token).ConfigureAwait(false)).IncomingTransfers.Select(Incoming).ToArray();
        return CliProtocol.Result(string.Join('\n', pending.Select(r => $"{r.Id}\t{r.Sender}\t{string.Join(", ", r.Files)}\t{r.Bytes}")),
            pending, CliJsonContext.Default.CliIncomingArray);
    }

    public async Task<CliResponse> AcceptAsync(CliRequest request, Guid id, string? destination,
        bool noChecksum, Action<CliResponse> emit, CancellationToken token)
    {
        var initial = await StateAsync(token).ConfigureAwait(false);
        if (!initial.IncomingTransfers.Any(r => r.RequestId == id))
            throw new CliException("Incoming request not found or already handled.", 3);
        var directory = destination is { } path
            ? CliPath.FullPath(path, request.WorkingDirectory)
            : initial.Settings.DownloadDirectory;
        // Check the destination before removing the GUI's pending request.
        Directory.CreateDirectory(directory);
        var state = await StateAsync(token).ConfigureAwait(false);
        var incoming = state.IncomingTransfers.FirstOrDefault(r => r.RequestId == id)
            ?? throw new CliException("Incoming request not found or already handled.", 3);
        var node = state.Node is { State: LocalSendNodeState.Running } running ? running : throw new CliException("Server is stopped.", 3);
        await runtime.DismissIncomingAsync(id, token).ConfigureAwait(false);
        var result = await node.AcceptAsync(id, new AcceptTransferOptions
        {
            DestinationDirectory = directory,
            VerifySha256 = !noChecksum && state.Settings.VerifyChecksumsOnReceive,
        }, new CliTransferProgress(emit), cancellationToken: token).ConfigureAwait(false);
        if (result is ReceiveOutcome.Completed completed && state.Settings.SaveReceiveHistory)
        {
            await runtime.RecordReceiveAsync(incoming, completed, token).ConfigureAwait(false);
        }
        return result switch
        {
            ReceiveOutcome.Completed complete => TransferResult(complete.TransferId, "Completed", complete.Items, 0),
            ReceiveOutcome.Cancelled cancelled => TransferResult(cancelled.TransferId, "Cancelled", cancelled.Items, 130),
            ReceiveOutcome.Failed failed => TransferResult(failed.TransferId, "Failed", failed.Items, 1, failed.Failure.Message),
        };
    }

    private static CliIncoming Incoming(IncomingTransferRequest request) => new(request.RequestId, request.TransferId,
        request.Sender.Alias, request.Sender.Fingerprint, request.Items.Select(i => i.FileName).ToArray(), request.Items.Sum(i => i.Size));

    private static CliResponse TransferResult(Guid id, string state, IReadOnlyList<TransferredItemResult> items, int exitCode, string? error = null) =>
        CliProtocol.Result($"Transfer {id}: {state}\n" + string.Join('\n', items.Select(i => i.SavedPath ?? i.FileName))
            + (error is null ? "" : "\n" + error), new CliTransfer(id, state, items.Select(i => i.SavedPath ?? i.FileName).ToArray(), error),
            CliJsonContext.Default.CliTransfer, exitCode);
}

sealed class CliTransferProgress(Action<CliResponse> emit) : IProgress<TransferProgress>
{
    private readonly Lock _gate = new();
    private long _last;
    private TransferState? _state;
    public void Report(TransferProgress value)
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;
            if (_state == value.State && now - _last < 200) return;
            _state = value.State;
            _last = now;
            emit(CliProtocol.Result($"{value.TransferId}\t{value.Direction}\t{value.State}\t{value.BytesTransferred}/{value.TotalBytes}",
                new CliProgress(value.TransferId, value.Direction.ToString(), value.State.ToString(), value.BytesTransferred, value.TotalBytes),
                CliJsonContext.Default.CliProgress) with
            { Type = "progress" });
        }
    }
}
