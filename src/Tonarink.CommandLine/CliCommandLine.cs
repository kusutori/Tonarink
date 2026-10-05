using System.CommandLine;
using System.CommandLine.Help;
using System.Globalization;

namespace Tonarink.Cli;

// System.CommandLine owns syntax, validation and dispatch. Handlers read the
// actual typed symbols and call services directly; no intermediate command DSL.
sealed class CliCommandLine
{
    public RootCommand Root { get; }
    public Option<bool> Json { get; }
    public Option<int?> Timeout { get; }
    public Option<bool> NoStart { get; }
    public Option<string> Language { get; }
    public Option<string> Profile { get; } = new("--profile") { Description = CliText.Get("Independent configuration/data directory"), Recursive = true };
    private readonly Command _status;
    private readonly Command _stop;
    private readonly Command _quit;
    private readonly HashSet<Command> _business = [];
    private readonly List<Option<string>> _textInputs = [];

    public CliCommandLine(bool integrated, Func<ParseResult, CancellationToken, Task<int>>? forward = null,
        ICliRuntime? runtime = null, CliRequest? request = null, Action<CliResponse>? emit = null)
    {
        Root = new(integrated ? CliText.Get("Tonarink CLI: reuse the desktop app's server and saved settings")
            : CliText.Get("Tonarink standalone CLI: manage an independent background LocalSend service"));
        foreach (var help in Root.Options.OfType<HelpOption>())
            help.Description = CliText.Get("Show help and usage information");
        Json = Flag(Root, "json", CliText.Get("Write progress and results as NDJSON"), true);
        Timeout = Seconds(Root, "timeout", CliText.Get("Cancel this command after N seconds (1-86400)"), true);
        NoStart = Flag(Root, "no-start", CliText.Get("Only connect to an already running host"), true);
        Language = Text(Root, "language", CliText.Get("CLI output language: system, zh-CN or en-US (default: system)"));
        Language.Recursive = true;
        Language.Validators.Add(p =>
        {
            if (!CliText.IsSupported(p.GetValueOrDefault<string>()))
                p.AddError(CliText.Get("Expected --language system, zh-CN or en-US."));
        });
        if (!integrated) Root.Options.Add(Profile);
        var commands = runtime is null ? null : new CliCommands(runtime);

        _status = Leaf(Root, "status", CliText.Get("Show server state without starting the host"), (_, ct) => commands!.StatusAsync(ct));
        foreach (var refresh in new[] { false, true })
        {
            var command = new Command(refresh ? "discover" : "devices", refresh ? CliText.Get("Refresh discovery and list nearby devices") : CliText.Get("List known devices"));
            var seconds = Seconds(command, "seconds", CliText.Get("Discovery duration (default: devices 1, discover 5)"));
            Attach(Root, command, (p, ct) => commands!.DevicesAsync(refresh, p.GetValue(seconds) ?? (refresh ? 5 : 1), ct));
        }
        var send = new Command("send", CliText.Get("Send files, folders or text to a device"));
        var target = Text(send, "target", CliText.Get("Device name, fingerprint, favorite or HTTP(S) IP address"), true);
        var (sendPaths, sendText) = Items(send);
        var sendPin = Text(send, "pin", CliText.Get("Receiver PIN (may be visible in process listings)"));
        var sendChecksum = Flag(send, "no-checksum", CliText.Get("Disable checksums for this transfer"));
        Attach(Root, send, (p, ct) => commands!.SendAsync(request!, p.GetValue(sendPaths) ?? [], p.GetValue(sendText),
            p.GetValue(target)!, p.GetValue(sendPin), p.GetValue(sendChecksum), emit!, ct));
        var cancel = new Command("cancel", CliText.Get("Cancel a pending or active transfer"));
        var transferId = Id(cancel, "transfer-id");
        Attach(Root, cancel, (p, ct) => commands!.CancelAsync(p.GetValue(transferId), ct));

        var receive = Group("receive", CliText.Get("Inspect and handle incoming requests"));
        Leaf(receive, "list", CliText.Get("List pending requests"), (_, ct) => commands!.IncomingAsync(ct));
        var accept = new Command("accept", CliText.Get("Accept all items in an incoming request"));
        var requestId = Id(accept, "request-id");
        var (acceptDirectory, acceptChecksum) = Destination(accept);
        Attach(receive, accept, (p, ct) => commands!.AcceptAsync(request!, p.GetValue(requestId), p.GetValue(acceptDirectory), p.GetValue(acceptChecksum), emit!, ct));
        var decline = new Command("decline", CliText.Get("Decline an incoming request"));
        var declineId = Id(decline, "request-id");
        Attach(receive, decline, (p, ct) => commands!.DeclineAsync(p.GetValue(declineId), ct));
        var watch = new Command("watch", CliText.Get("Watch incoming requests until cancelled or the duration expires"));
        var autoAccept = Flag(watch, "auto-accept", CliText.Get("Accept incoming requests while watching"));
        var watchSeconds = Seconds(watch, "seconds", CliText.Get("Watch duration; omit to watch until Ctrl+C"));
        var (watchDirectory, watchChecksum) = Destination(watch);
        Attach(receive, watch, (p, ct) => commands!.WatchAsync(request!, p.GetValue(watchSeconds), p.GetValue(autoAccept),
            p.GetValue(watchDirectory), p.GetValue(watchChecksum), emit!, ct));

        var server = Group("server", CliText.Get("Control the LocalSend server"));
        Leaf(server, "start", CliText.Get("Start the server"), (_, ct) => runtime!.ServerAsync(CliServerAction.Start, ct));
        _stop = Leaf(server, "stop", CliText.Get("Stop the server"), (_, ct) => runtime!.ServerAsync(CliServerAction.Stop, ct));
        Leaf(server, "restart", CliText.Get("Restart the server (interrupts active transfers)"), (_, ct) => runtime!.ServerAsync(CliServerAction.Restart, ct));
        var settings = Group("settings", CliText.Get("Read or persist host settings"));
        foreach (var single in new[] { false, true })
        {
            var command = new Command(single ? "get" : "list", single ? CliText.Get("Read one setting") : CliText.Get("List supported settings"));
            var key = single ? Word(command, "key", CliText.Get("Setting key")) : null;
            var secrets = Flag(command, "show-secrets", CliText.Get("Include the receiving PIN"));
            Attach(settings, command, (p, ct) => commands!.SettingsAsync(key is null ? null : p.GetValue(key), p.GetValue(secrets), ct));
        }
        var set = new Command("set", CliText.Get("Validate and save one setting"));
        var settingKey = Word(set, "key", CliText.Get("Setting key")); var value = Word(set, "value", CliText.Get("Setting value; quote paths and empty strings"));
        var restart = Flag(set, "restart", CliText.Get("Restart using the saved settings"));
        Attach(settings, set, (p, ct) => runtime!.SaveSettingAsync(p.GetValue(settingKey)!, p.GetValue(value)!, request!.WorkingDirectory, p.GetValue(restart), ct));

        var favorites = Group("favorites", CliText.Get("Manage saved device identities"));
        Leaf(favorites, "list", CliText.Get("List saved favorites"), (_, ct) => commands!.FavoritesAsync(ct));
        var add = new Command("add", CliText.Get("Probe and save a device")); var favoriteTarget = Word(add, "target", CliText.Get("Device name, fingerprint or address"));
        Attach(favorites, add, (p, ct) => commands!.AddFavoriteAsync(p.GetValue(favoriteTarget)!, ct));
        var remove = new Command("remove", CliText.Get("Remove a favorite")); var fingerprint = Word(remove, "fingerprint", CliText.Get("Complete saved fingerprint"));
        Attach(favorites, remove, (p, ct) => commands!.RemoveFavoriteAsync(p.GetValue(fingerprint)!, ct));
        var history = Group("history", CliText.Get("Manage receive history without deleting saved files"));
        Leaf(history, "list", CliText.Get("List receive history"), (_, ct) => commands!.HistoryAsync(ct));
        var historyRemove = new Command("remove", CliText.Get("Remove one history record")); var historyId = Id(historyRemove, "history-id");
        Attach(history, historyRemove, (p, ct) => commands!.RemoveHistoryAsync(p.GetValue(historyId), ct));
        var clear = new Command("clear", CliText.Get("Clear history; saved files are retained")); Confirm(clear);
        Attach(history, clear, (_, ct) => commands!.RemoveHistoryAsync(null, ct));

        var web = Group("web", CliText.Get("Manage browser sharing"));
        var webSend = new Command("send", CliText.Get("Share files, folders or text in a browser")); var (webPaths, webText) = Items(webSend);
        var (webSendAuto, webSendPin) = WebOptions(webSend);
        Attach(web, webSend, (p, ct) => commands!.WebSendAsync(request!, p.GetValue(webPaths) ?? [], p.GetValue(webText), p.GetValue(webSendAuto), p.GetValue(webSendPin), ct));
        var webReceive = new Command("receive", CliText.Get("Receive uploads from a browser")); var (webReceiveAuto, webReceivePin) = WebOptions(webReceive);
        Attach(web, webReceive, (p, ct) => commands!.WebReceiveAsync(p.GetValue(webReceiveAuto), p.GetValue(webReceivePin), ct));
        Leaf(web, "status", CliText.Get("Show browser sharing state and links"), (_, ct) => commands!.WebStatusAsync(ct));
        Leaf(web, "stop", CliText.Get("Stop browser sharing"), (_, ct) => commands!.WebStopAsync(ct));
        foreach (var approved in new[] { true, false })
        {
            var command = new Command(approved ? "accept" : "decline", approved ? CliText.Get("Approve a browser request") : CliText.Get("Reject a browser request"));
            var session = Word(command, "session-id", CliText.Get("Browser session ID"));
            Attach(web, command, (p, ct) => commands!.WebDecisionAsync(p.GetValue(session)!, approved, ct));
        }
        var app = Group("app", integrated ? CliText.Get("Control the desktop application") : CliText.Get("Control the independent CLI host"));
        if (integrated)
        {
            var open = new Command("open", CliText.Get("Show the main window, a favorite device or a history entry"));
            var favorite = Text(open, "favorite", CliText.Get("Open a saved device by fingerprint"));
            var entry = new Option<Guid?>("--history")
            {
                Description = CliText.Get("Locate a receive history entry by UUID"),
                CustomParser = p => Guid.TryParse(p.Tokens.SingleOrDefault()?.Value, out var id) ? id
                    : Invalid<Guid?>(() => p.AddError(CliText.Get("Expected a UUID for {0}.", "--history"))),
            }; open.Options.Add(entry);
            open.Validators.Add(p =>
            {
                if (p.GetValue(favorite) is not null && p.GetResult(entry)?.Tokens.Count > 0)
                    p.AddError(CliText.Get("Do not combine --favorite and --history."));
            });
            Attach(app, open, async (p, ct) => { await runtime!.OpenAppAsync(p.GetValue(favorite), p.GetValue(entry), ct).ConfigureAwait(false); return CliProtocol.Success(CliText.Get("Main window opened")); });
        }
        _quit = new("quit", CliText.Get("Exit the host and stop its services")); Confirm(_quit);
        Attach(app, _quit, (_, _) => Task.FromResult(CliProtocol.Success(CliText.Get("Host is exiting"))));

        Command Group(string name, string description) { var command = new Command(name, description); Root.Subcommands.Add(command); return command; }
        Command Leaf(Command parent, string name, string description, Func<ParseResult, CancellationToken, Task<CliResponse>> action)
        { var command = new Command(name, description); Attach(parent, command, action); return command; }
        void Attach(Command parent, Command command, Func<ParseResult, CancellationToken, Task<CliResponse>> action)
        {
            parent.Subcommands.Add(command); _business.Add(command);
            command.SetAction(forward ?? (async (parse, ct) =>
            {
                CliResponse response;
                try { response = await action(parse, ct).ConfigureAwait(false); }
                catch (CliException exception) { response = CliProtocol.Error(exception.Message, exception.ExitCode); }
                catch (OperationCanceledException) { response = CliProtocol.Error(CliText.Get("Command cancelled or timed out"), 130); }
                catch (Exception exception) { response = CliProtocol.Error(exception.Message); }
                emit?.Invoke(response); return response.ExitCode;
            }));
        }
    }

    public bool IsBusinessInvocation(ParseResult p) => _business.Contains(p.CommandResult.Command);
    public bool IsStatus(ParseResult p) => p.CommandResult.Command == _status;
    public bool IsQuit(ParseResult p) => p.CommandResult.Command == _quit && p.Action == _quit.Action;
    public bool CanRunWithoutHost(ParseResult p) => IsStatus(p) || IsQuit(p) || p.CommandResult.Command == _stop;
    public bool ReadsStandardInput(ParseResult p) => _textInputs.Any(option => p.GetValue(option) == "-");

    private static Option<bool> Flag(Command c, string name, string description, bool recursive = false)
    { var option = new Option<bool>("--" + name) { Description = description, Recursive = recursive }; c.Options.Add(option); return option; }
    private static Option<string> Text(Command c, string name, string description, bool required = false)
    { var option = new Option<string>("--" + name) { Description = description, Required = required }; c.Options.Add(option); return option; }
    private static Option<int?> Seconds(Command c, string name, string description, bool recursive = false)
    {
        var option = new Option<int?>("--" + name)
        {
            Description = description, Recursive = recursive,
            CustomParser = p =>
            {
                if (!int.TryParse(p.Tokens.SingleOrDefault()?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    return Invalid<int?>(() => p.AddError(CliText.Get("Expected an integer for {0}.", "--" + name)));
                if (value is < 1 or > 86400)
                    return Invalid<int?>(() => p.AddError(CliText.Get("--{0} must be between 1 and 86400.", name)));
                return value;
            },
        };
        c.Options.Add(option); return option;
    }
    private static Argument<string> Word(Command c, string name, string description)
    { var argument = new Argument<string>(name) { Description = description }; c.Arguments.Add(argument); return argument; }
    private static Argument<Guid> Id(Command c, string name)
    {
        var argument = new Argument<Guid>(name)
        {
            Description = CliText.Get("UUID reported by the corresponding list or progress command"),
            CustomParser = p => Guid.TryParse(p.Tokens.SingleOrDefault()?.Value, out var id) ? id
                : Invalid<Guid>(() => p.AddError(CliText.Get("Expected a UUID for {0}.", name))),
        };
        c.Arguments.Add(argument); return argument;
    }
    private static T? Invalid<T>(Action report) { report(); return default; }
    private (Argument<string[]> Paths, Option<string> Text) Items(Command c)
    {
        var paths = new Argument<string[]>("paths") { Description = CliText.Get("Files or folders; relative to the caller's working directory"), Arity = ArgumentArity.ZeroOrMore };
        var text = Text(c, "text", CliText.Get("Send literal text; use '-' for redirected stdin")); c.Arguments.Add(paths); _textInputs.Add(text);
        c.Validators.Add(p => { var count = p.GetValue(paths)?.Length ?? 0;
            if (count == 0 && p.GetValue(text) is null) p.AddError(CliText.Get("Provide paths or --text TEXT."));
            if (count > 0 && p.GetValue(text) is not null) p.AddError(CliText.Get("Do not combine paths with --text.")); });
        return (paths, text);
    }
    private static void Confirm(Command c)
    { var yes = Flag(c, "yes", CliText.Get("Confirm this action")); c.Validators.Add(p => { if (!p.GetValue(yes)) p.AddError(CliText.Get("This action requires --yes.")); }); }
    private static (Option<string>, Option<bool>) Destination(Command c) => (Text(c, "directory", CliText.Get("Receive destination for this invocation")), Flag(c, "no-checksum", CliText.Get("Disable checksum verification")));
    private static (Option<bool>, Option<string>) WebOptions(Command c) => (Flag(c, "auto-accept", CliText.Get("Approve browser requests automatically")), Text(c, "pin", CliText.Get("Protect browser sharing with a PIN")));
}
