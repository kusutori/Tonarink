using LocalSendDotNet;
using Microsoft.Extensions.Logging;

return await Cli.RunAsync(args).ConfigureAwait(false);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(args.Contains("--verbose", StringComparer.Ordinal) ? LogLevel.Debug : LogLevel.Information)
            .AddSimpleConsole(options => options.SingleLine = true));

        try
        {
            var parsed = Arguments.Parse(args[1..]);
            var nodeOptions = CreateOptions(parsed);
            await using var node = new LocalSendNode(nodeOptions, loggerFactory);
            return args[0] switch
            {
                "discover" => await DiscoverAsync(node, parsed, stop.Token).ConfigureAwait(false),
                "listen" => await ListenAsync(node, parsed, stop.Token).ConfigureAwait(false),
                "send" => await SendFilesAsync(node, parsed, stop.Token).ConfigureAwait(false),
                "send-dir" => await SendDirectoryAsync(node, parsed, stop.Token).ConfigureAwait(false),
                "send-text" => await SendTextAsync(node, parsed, stop.Token).ConfigureAwait(false),
                _ => UnknownCommand(args[0])
            };
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static LocalSendOptions CreateOptions(Arguments args)
    {
        var data = args.Value("data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalSendDotNet");
        var downloads = args.Value("download-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return new LocalSendOptions
        {
            Alias = args.Value("alias") ?? Environment.MachineName,
            DeviceModel = $"{Environment.OSVersion.Platform} (.NET)",
            DeviceType = LocalSendDeviceType.Headless,
            DataDirectory = data,
            DownloadDirectory = downloads,
            Port = int.TryParse(args.Value("port"), out var port) ? port : LocalSendOptions.DefaultPort,
            ReceivePin = args.Value("receive-pin")
        };
    }

    private static async Task<int> DiscoverAsync(LocalSendNode node, Arguments args, CancellationToken cancellationToken)
    {
        await node.StartAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(args.IntValue("seconds", 5)), cancellationToken).ConfigureAwait(false);
        foreach (var device in node.GetDevices())
            PrintDevice(device);
        return 0;
    }

    private static async Task<int> ListenAsync(LocalSendNode node, Arguments args, CancellationToken cancellationToken)
    {
        await node.StartAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Listening for LocalSend transfers. Press Ctrl+C to stop.");
        await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine($"Incoming from {request.Sender.Alias}:");
            foreach (var item in request.Items) Console.WriteLine($"  {item.Id}  {item.FileName}  {item.Size} bytes");
            var accept = args.Has("auto-accept") || AskYesNo("Accept all files? [y/N] ");
            if (!accept)
            {
                await node.DeclineAsync(request.RequestId, cancellationToken).ConfigureAwait(false);
                continue;
            }
            _ = ReceiveAndReportAsync(node, request, cancellationToken);
        }
        return 0;
    }

    private static async Task ReceiveAndReportAsync(LocalSendNode node, IncomingTransferRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var progress = new Progress<TransferProgress>(PrintProgress);
            var result = await node.AcceptAsync(request.RequestId, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false);
            switch (result)
            {
                case ReceiveOutcome.Completed completed:
                    Console.WriteLine($"Receive completed: {completed.Items.Count} item(s)");
                    foreach (var item in completed.Items) Console.WriteLine($"  {item.SavedPath}");
                    break;
                case ReceiveOutcome.Cancelled cancelled:
                    Console.WriteLine($"Receive cancelled: {cancelled.Items.Count} item(s)");
                    break;
                case ReceiveOutcome.Failed failed:
                    Console.Error.WriteLine($"Receive failed: {failed.Failure.Message}");
                    break;
            }
        }
        catch (Exception exception) { Console.Error.WriteLine($"receive error: {exception.Message}"); }
    }

    private static async Task<int> SendFilesAsync(LocalSendNode node, Arguments args, CancellationToken cancellationToken)
    {
        var target = args.Value("target") ?? throw new ArgumentException("send requires --target <alias|fingerprint>.");
        if (args.Positionals.Count == 0) throw new ArgumentException("send requires at least one file path.");
        var items = args.Positionals.Select(path => (SendItem)new SendFileItem(path)).ToArray();
        return await SendAsync(node, target, items, args.Value("pin"), args.Has("sha256"), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> SendDirectoryAsync(LocalSendNode node, Arguments args, CancellationToken cancellationToken)
    {
        var target = args.Value("target") ?? throw new ArgumentException("send-dir requires --target <alias|fingerprint>.");
        if (args.Positionals.Count != 1) throw new ArgumentException("send-dir requires exactly one directory path.");
        var items = LocalSendItems.FromDirectory(args.Positionals[0]).Cast<SendItem>().ToArray();
        if (items.Length == 0) throw new ArgumentException("The directory contains no files.");
        return await SendAsync(node, target, items, args.Value("pin"), args.Has("sha256"), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> SendTextAsync(LocalSendNode node, Arguments args, CancellationToken cancellationToken)
    {
        var target = args.Value("target") ?? throw new ArgumentException("send-text requires --target <alias|fingerprint>.");
        if (args.Positionals.Count == 0) throw new ArgumentException("send-text requires text.");
        return await SendAsync(node, target, [new SendTextItem(string.Join(' ', args.Positionals))], args.Value("pin"), args.Has("sha256"), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> SendAsync(LocalSendNode node, string target, IReadOnlyCollection<SendItem> items, string? pin, bool computeSha256, CancellationToken cancellationToken)
    {
        await node.StartAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        var device = node.GetDevices().FirstOrDefault(d =>
            d.Alias.Equals(target, StringComparison.OrdinalIgnoreCase) || d.Fingerprint.Equals(target, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Device '{target}' was not discovered.");
        PrintDevice(device);
        var result = await node.SendAsync(device, items, new SendOptions { Pin = pin, ComputeSha256 = computeSha256 }, new Progress<TransferProgress>(PrintProgress), cancellationToken).ConfigureAwait(false);
        return result switch
        {
            SendOutcome.Completed completed => ReportSend("completed", completed.Items, exitCode: 0),
            SendOutcome.Cancelled cancelled => ReportSend("cancelled", cancelled.Items, exitCode: 1),
            SendOutcome.PinRequired required => ReportSendFailure(required.InvalidPin
                ? "The supplied PIN is incorrect."
                : "The receiving device requires a PIN."),
            SendOutcome.PinRateLimited => ReportSendFailure("Too many incorrect PIN attempts. Try again later."),
            SendOutcome.PeerBusy => ReportSendFailure("The receiving device is busy."),
            SendOutcome.Declined => ReportSendFailure("The receiving device declined the transfer."),
            SendOutcome.Failed failed => ReportSendFailure(failed.Failure.Message),
        };

        static int ReportSend(string status, IReadOnlyList<TransferredItemResult> transferred, int exitCode)
        {
            Console.WriteLine($"Transfer {status}: {transferred.Count} item(s)");
            return exitCode;
        }

        static int ReportSendFailure(string message)
        {
            Console.Error.WriteLine(message);
            return 1;
        }
    }

    private static void PrintProgress(TransferProgress progress) => Console.WriteLine($"{progress.Direction,-7} {progress.State,-20} {progress.BytesTransferred}/{progress.TotalBytes}");
    private static void PrintDevice(LocalSendDevice device) => Console.WriteLine($"{device.Alias}  {device.Fingerprint}  {string.Join(", ", device.Endpoints)}  v{device.ProtocolVersion}");
    private static bool AskYesNo(string prompt) { Console.Write(prompt); return Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true; }
    private static int UnknownCommand(string command) { Console.Error.WriteLine($"Unknown command: {command}"); PrintHelp(); return 2; }

    private static void PrintHelp() => Console.WriteLine("""
        LocalSendDotNet diagnostic CLI

          discover [--seconds 5]
          listen [--auto-accept] [--receive-pin PIN]
          send --target ALIAS_OR_FINGERPRINT [--pin PIN] FILE...
          send-dir --target ALIAS_OR_FINGERPRINT [--pin PIN] DIRECTORY
          send-text --target ALIAS_OR_FINGERPRINT [--pin PIN] TEXT...

        Common options: --alias NAME --port PORT --data-dir PATH --download-dir PATH --sha256 --verbose
        """);
}

internal sealed class Arguments
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);
    public List<string> Positionals { get; } = [];
    public bool Has(string name) => _options.ContainsKey(name);
    public string? Value(string name) => _options.GetValueOrDefault(name);
    public int IntValue(string name, int fallback) => int.TryParse(Value(name), out var value) ? value : fallback;

    public static Arguments Parse(string[] args)
    {
        var result = new Arguments();
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)) { result.Positionals.Add(args[index]); continue; }
            var name = args[index][2..];
            if (name is "auto-accept" or "sha256" or "verbose") { result._options[name] = null; continue; }
            string? value = null;
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) value = args[++index];
            result._options[name] = value;
        }
        return result;
    }
}
