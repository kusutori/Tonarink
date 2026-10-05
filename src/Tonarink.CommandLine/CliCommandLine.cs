using System.CommandLine;

namespace Tonarink.Cli;

// System.CommandLine owns syntax, validation and dispatch. Handlers read the
// actual typed symbols and call services directly; no intermediate command DSL.
sealed class CliCommandLine
{
    public RootCommand Root { get; }
    public Option<bool> Json { get; }
    public Option<int?> Timeout { get; }
    public Option<bool> NoStart { get; }
    public Option<string> Profile { get; } = new("--profile") { Description = "Independent configuration/data directory", Recursive = true };
    private readonly Command _status;
    private readonly Command _stop;
    private readonly Command _quit;
    private readonly HashSet<Command> _business = [];
    private readonly List<Option<string>> _textInputs = [];

    public CliCommandLine(bool integrated, Func<ParseResult, CancellationToken, Task<int>>? forward = null,
        ICliRuntime? runtime = null, CliRequest? request = null, Action<CliResponse>? emit = null)
    {
        Root = new(integrated ? "Tonarink CLI: reuse the desktop app's server and saved settings"
            : "Tonarink standalone CLI: manage an independent background LocalSend service");
        Json = Flag(Root, "json", "Write progress and results as NDJSON", true);
        Timeout = Seconds(Root, "timeout", "Cancel this command after N seconds (1-86400)", true);
        NoStart = Flag(Root, "no-start", "Only connect to an already running host", true);
        if (!integrated) Root.Options.Add(Profile);
        var commands = runtime is null ? null : new CliCommands(runtime);

        _status = Leaf(Root, "status", "Show server state without starting the host", (_, ct) => commands!.StatusAsync(ct));
        foreach (var refresh in new[] { false, true })
        {
            var command = new Command(refresh ? "discover" : "devices", refresh ? "Refresh discovery and list nearby devices" : "List known devices");
            var seconds = Seconds(command, "seconds", "Discovery duration (default: devices 1, discover 5)");
            Attach(Root, command, (p, ct) => commands!.DevicesAsync(refresh, p.GetValue(seconds) ?? (refresh ? 5 : 1), ct));
        }
        var send = new Command("send", "Send files, folders or text to a device");
        var target = Text(send, "target", "Device name, fingerprint, favorite or HTTP(S) IP address", true);
        var (sendPaths, sendText) = Items(send);
        var sendPin = Text(send, "pin", "Receiver PIN (may be visible in process listings)");
        var sendChecksum = Flag(send, "no-checksum", "Disable checksums for this transfer");
        Attach(Root, send, (p, ct) => commands!.SendAsync(request!, p.GetValue(sendPaths) ?? [], p.GetValue(sendText),
            p.GetValue(target)!, p.GetValue(sendPin), p.GetValue(sendChecksum), emit!, ct));
        var cancel = new Command("cancel", "Cancel a pending or active transfer");
        var transferId = Id(cancel, "transfer-id");
        Attach(Root, cancel, (p, ct) => commands!.CancelAsync(p.GetValue(transferId), ct));

        var receive = Group("receive", "Inspect and handle incoming requests");
        Leaf(receive, "list", "List pending requests", (_, ct) => commands!.IncomingAsync(ct));
        var accept = new Command("accept", "Accept all items in an incoming request");
        var requestId = Id(accept, "request-id");
        var (acceptDirectory, acceptChecksum) = Destination(accept);
        Attach(receive, accept, (p, ct) => commands!.AcceptAsync(request!, p.GetValue(requestId), p.GetValue(acceptDirectory), p.GetValue(acceptChecksum), emit!, ct));
        var decline = new Command("decline", "Decline an incoming request");
        var declineId = Id(decline, "request-id");
        Attach(receive, decline, (p, ct) => commands!.DeclineAsync(p.GetValue(declineId), ct));
        var watch = new Command("watch", "Watch incoming requests until cancelled or the duration expires");
        var autoAccept = Flag(watch, "auto-accept", "Accept incoming requests while watching");
        var watchSeconds = Seconds(watch, "seconds", "Watch duration; omit to watch until Ctrl+C");
        var (watchDirectory, watchChecksum) = Destination(watch);
        Attach(receive, watch, (p, ct) => commands!.WatchAsync(request!, p.GetValue(watchSeconds), p.GetValue(autoAccept),
            p.GetValue(watchDirectory), p.GetValue(watchChecksum), emit!, ct));

        var server = Group("server", "Control the LocalSend server");
        Leaf(server, "start", "Start the server", (_, ct) => runtime!.ServerAsync(CliServerAction.Start, ct));
        _stop = Leaf(server, "stop", "Stop the server", (_, ct) => runtime!.ServerAsync(CliServerAction.Stop, ct));
        Leaf(server, "restart", "Restart the server (interrupts active transfers)", (_, ct) => runtime!.ServerAsync(CliServerAction.Restart, ct));
        var settings = Group("settings", "Read or persist host settings");
        foreach (var single in new[] { false, true })
        {
            var command = new Command(single ? "get" : "list", single ? "Read one setting" : "List supported settings");
            var key = single ? Word(command, "key", "Setting key") : null;
            var secrets = Flag(command, "show-secrets", "Include the receiving PIN");
            Attach(settings, command, (p, ct) => commands!.SettingsAsync(key is null ? null : p.GetValue(key), p.GetValue(secrets), ct));
        }
        var set = new Command("set", "Validate and save one setting");
        var settingKey = Word(set, "key", "Setting key"); var value = Word(set, "value", "Setting value; quote paths and empty strings");
        var restart = Flag(set, "restart", "Restart using the saved settings");
        Attach(settings, set, (p, ct) => runtime!.SaveSettingAsync(p.GetValue(settingKey)!, p.GetValue(value)!, request!.WorkingDirectory, p.GetValue(restart), ct));

        var favorites = Group("favorites", "Manage saved device identities");
        Leaf(favorites, "list", "List saved favorites", (_, ct) => commands!.FavoritesAsync(ct));
        var add = new Command("add", "Probe and save a device"); var favoriteTarget = Word(add, "target", "Device name, fingerprint or address");
        Attach(favorites, add, (p, ct) => commands!.AddFavoriteAsync(p.GetValue(favoriteTarget)!, ct));
        var remove = new Command("remove", "Remove a favorite"); var fingerprint = Word(remove, "fingerprint", "Complete saved fingerprint");
        Attach(favorites, remove, (p, ct) => commands!.RemoveFavoriteAsync(p.GetValue(fingerprint)!, ct));
        var history = Group("history", "Manage receive history without deleting saved files");
        Leaf(history, "list", "List receive history", (_, ct) => commands!.HistoryAsync(ct));
        var historyRemove = new Command("remove", "Remove one history record"); var historyId = Id(historyRemove, "history-id");
        Attach(history, historyRemove, (p, ct) => commands!.RemoveHistoryAsync(p.GetValue(historyId), ct));
        var clear = new Command("clear", "Clear history; saved files are retained"); Confirm(clear);
        Attach(history, clear, (_, ct) => commands!.RemoveHistoryAsync(null, ct));

        var web = Group("web", "Manage browser sharing");
        var webSend = new Command("send", "Share files, folders or text in a browser"); var (webPaths, webText) = Items(webSend);
        var (webSendAuto, webSendPin) = WebOptions(webSend);
        Attach(web, webSend, (p, ct) => commands!.WebSendAsync(request!, p.GetValue(webPaths) ?? [], p.GetValue(webText), p.GetValue(webSendAuto), p.GetValue(webSendPin), ct));
        var webReceive = new Command("receive", "Receive uploads from a browser"); var (webReceiveAuto, webReceivePin) = WebOptions(webReceive);
        Attach(web, webReceive, (p, ct) => commands!.WebReceiveAsync(p.GetValue(webReceiveAuto), p.GetValue(webReceivePin), ct));
        Leaf(web, "status", "Show browser sharing state and links", (_, ct) => commands!.WebStatusAsync(ct));
        Leaf(web, "stop", "Stop browser sharing", (_, ct) => commands!.WebStopAsync(ct));
        foreach (var approved in new[] { true, false })
        {
            var command = new Command(approved ? "accept" : "decline", approved ? "Approve a browser request" : "Reject a browser request");
            var session = Word(command, "session-id", "Browser session ID");
            Attach(web, command, (p, ct) => commands!.WebDecisionAsync(p.GetValue(session)!, approved, ct));
        }
        var app = Group("app", integrated ? "Control the desktop application" : "Control the independent CLI host");
        if (integrated)
        {
            var open = new Command("open", "Show the main window, a favorite device or a history entry");
            var favorite = Text(open, "favorite", "Open a saved device by fingerprint");
            var entry = new Option<Guid?>("--history") { Description = "Locate a receive history entry by UUID" }; open.Options.Add(entry);
            open.Validators.Add(p => { if (p.GetValue(favorite) is not null && p.GetValue(entry) is not null) p.AddError("Do not combine --favorite and --history."); });
            Attach(app, open, async (p, ct) => { await runtime!.OpenAppAsync(p.GetValue(favorite), p.GetValue(entry), ct).ConfigureAwait(false); return CliProtocol.Success("Main window opened"); });
        }
        _quit = new("quit", "Exit the host and stop its services"); Confirm(_quit);
        Attach(app, _quit, (_, _) => Task.FromResult(CliProtocol.Success("Host is exiting")));

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
                catch (OperationCanceledException) { response = CliProtocol.Error("Command cancelled or timed out", 130); }
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
        var option = new Option<int?>("--" + name) { Description = description, Recursive = recursive };
        option.Validators.Add(p => { if (p.GetValueOrDefault<int?>() is < 1 or > 86400) p.AddError($"--{name} must be between 1 and 86400."); });
        c.Options.Add(option); return option;
    }
    private static Argument<string> Word(Command c, string name, string description)
    { var argument = new Argument<string>(name) { Description = description }; c.Arguments.Add(argument); return argument; }
    private static Argument<Guid> Id(Command c, string name)
    { var argument = new Argument<Guid>(name) { Description = "UUID reported by the corresponding list or progress command" }; c.Arguments.Add(argument); return argument; }
    private (Argument<string[]> Paths, Option<string> Text) Items(Command c)
    {
        var paths = new Argument<string[]>("paths") { Description = "Files or folders; relative to the caller's working directory", Arity = ArgumentArity.ZeroOrMore };
        var text = Text(c, "text", "Send literal text; use '-' for redirected stdin"); c.Arguments.Add(paths); _textInputs.Add(text);
        c.Validators.Add(p => { var count = p.GetValue(paths)?.Length ?? 0;
            if (count == 0 && p.GetValue(text) is null) p.AddError("Provide paths or --text TEXT.");
            if (count > 0 && p.GetValue(text) is not null) p.AddError("Do not combine paths with --text."); });
        return (paths, text);
    }
    private static void Confirm(Command c)
    { var yes = Flag(c, "yes", "Confirm this action"); c.Validators.Add(p => { if (!p.GetValue(yes)) p.AddError("This action requires --yes."); }); }
    private static (Option<string>, Option<bool>) Destination(Command c) => (Text(c, "directory", "Receive destination for this invocation"), Flag(c, "no-checksum", "Disable checksum verification"));
    private static (Option<bool>, Option<string>) WebOptions(Command c) => (Flag(c, "auto-accept", "Approve browser requests automatically"), Text(c, "pin", "Protect browser sharing with a PIN"));
}
