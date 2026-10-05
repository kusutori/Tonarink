using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace Tonarink.Cli;

// A bound invocation, not a second parser. All tokenization, arity, option
// types, validation, subcommands and help are owned by System.CommandLine.
sealed record CliArguments(string Command, string Action, List<string> Positionals,
    Dictionary<string, string?> Options)
{
    public bool Has(string key) => Options.ContainsKey(key);
    public string? Value(string key) => Options.GetValueOrDefault(key);
    public string Required(string key) => Value(key) ?? throw new CliException($"Missing --{key}.");
    public bool Json => Has("json");
    public int Integer(string key, int fallback) => Value(key) is { } value
        ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
}

sealed class CliCommandLine
{
    public RootCommand Root { get; }
    public Option<bool> Json { get; }
    private readonly Dictionary<Command, (string Command, string Action)> _commands = [];
    private readonly Dictionary<Symbol, Func<ParseResult, string?>> _options = [];
    private readonly Dictionary<Argument, Func<ParseResult, IEnumerable<string>>> _arguments = [];

    public CliCommandLine(bool integrated, Func<ParseResult, CancellationToken, Task<int>>? invoke = null)
    {
        Root = new(integrated
            ? "Tonarink CLI: reuse the desktop app's server and saved settings"
            : "Tonarink standalone CLI: manage an independent background LocalSend service");
        Json = Flag(Root, "json", "Write progress and results as NDJSON", recursive: true);
        Number(Root, "timeout", "Cancel this command after N seconds (1-86400)", recursive: true);
        Flag(Root, "no-start", "Only connect to an already running host", recursive: true);
        if (!integrated) Text(Root, "profile", "Independent configuration/data directory", recursive: true);

        Leaf(Root, "status", "Show application and server state without starting the host");
        foreach (var name in new[] { "devices", "discover" })
            Number(Leaf(Root, name, name == "discover" ? "Refresh discovery and list nearby devices" : "List known devices"),
                "seconds", "Wait for discovery for N seconds (default: devices 1, discover 5)");
        var send = Leaf(Root, "send", "Send files, folders or text to a device");
        Text(send, "target", "Device name, complete fingerprint, favorite or HTTP(S) IP address", required: true);
        Items(send); Text(send, "pin", "Receiver PIN (may be visible in process listings)");
        Flag(send, "no-checksum", "Disable checksums for this transfer");
        Id(Leaf(Root, "cancel", "Cancel a pending or active transfer"), "transfer-id");

        var receive = Group("receive", "Inspect and handle incoming requests");
        Leaf(receive, "list", "List pending requests and transfer IDs");
        var accept = Leaf(receive, "accept", "Accept all items in an incoming request");
        Id(accept, "request-id"); Destination(accept);
        Id(Leaf(receive, "decline", "Decline an incoming request"), "request-id");
        var watch = Leaf(receive, "watch", "Watch incoming requests until cancelled or the duration expires");
        Flag(watch, "auto-accept", "Accept incoming requests while this watcher is running");
        Number(watch, "seconds", "Watch duration; omit to watch until Ctrl+C"); Destination(watch);

        var server = Group("server", "Control the LocalSend server");
        foreach (var action in new[] { "start", "stop", "restart" })
            Leaf(server, action, action + " the server (restart interrupts active transfers)");
        var settings = Group("settings", "Read or persist host settings");
        Flag(Leaf(settings, "list", "List supported settings"), "show-secrets", "Include the receiving PIN");
        var get = Leaf(settings, "get", "Read one setting"); Word(get, "key", "Setting key");
        Flag(get, "show-secrets", "Include the receiving PIN");
        var set = Leaf(settings, "set", "Validate and save one setting");
        Word(set, "key", "Setting key"); Word(set, "value", "Setting value; quote paths and empty strings");
        Flag(set, "restart", "Restart the server using the saved settings");

        var favorites = Group("favorites", "Manage saved device identities");
        Leaf(favorites, "list", "List saved favorites");
        Word(Leaf(favorites, "add", "Probe and save a device"), "target", "Device name, fingerprint or address");
        Word(Leaf(favorites, "remove", "Remove a favorite"), "fingerprint", "Complete saved fingerprint");
        var history = Group("history", "Manage receive history without deleting saved files");
        Leaf(history, "list", "List receive history");
        Id(Leaf(history, "remove", "Remove one history record"), "history-id");
        Confirm(Leaf(history, "clear", "Clear history; saved files are retained"));

        var web = Group("web", "Manage browser sharing");
        var webSend = Leaf(web, "send", "Share files, folders or text in a browser"); Items(webSend); WebOptions(webSend);
        WebOptions(Leaf(web, "receive", "Receive uploads from a browser"));
        Leaf(web, "status", "Show browser sharing state and links"); Leaf(web, "stop", "Stop browser sharing");
        Word(Leaf(web, "accept", "Approve a browser request"), "session-id", "Browser session ID");
        Word(Leaf(web, "decline", "Reject a browser request"), "session-id", "Browser session ID");
        var app = Group("app", integrated ? "Control the desktop application" : "Control the independent CLI host");
        if (integrated) Leaf(app, "open", "Show the desktop application's main window");
        Confirm(Leaf(app, "quit", "Exit the host and stop its services"));

        // Built-in --help/--version actions remain library-owned. Only business
        // command actions invoke the transport; help never starts a host.
        if (invoke is not null)
            foreach (var command in _commands.Keys) command.SetAction(invoke);
    }

    public CliArguments Bind(ParseResult result)
    {
        if (result.Errors.Count != 0) throw new CliException(string.Join('\n', result.Errors.Select(e => e.Message)));
        if (!_commands.TryGetValue(result.CommandResult.Command, out var path))
            throw new CliException("A subcommand is required. Use --help for available commands.");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (symbol, read) in _options)
            if (result.GetResult(symbol) is OptionResult { Implicit: false } && read(result) is { } value)
                values[symbol.Name.TrimStart('-')] = value;
        var positions = result.CommandResult.Command.Arguments.SelectMany(argument => _arguments[argument](result)).ToList();
        return new(path.Command, path.Action, positions, values);
    }

    public CliArguments Parse(string[] arguments) => Bind(Root.Parse(arguments));

    private Command Group(string name, string description)
    { var command = new Command(name, description); Root.Subcommands.Add(command); return command; }
    private Command Leaf(Command parent, string name, string description)
    {
        var command = new Command(name, description); parent.Subcommands.Add(command);
        _commands.Add(command, parent == Root ? (name, "") : (parent.Name, name));
        return command;
    }
    private Option<bool> Flag(Command command, string name, string description, bool recursive = false)
    {
        var option = new Option<bool>("--" + name) { Description = description, Recursive = recursive };
        command.Options.Add(option); _options.Add(option, parse => parse.GetValue(option) ? "true" : null);
        return option;
    }
    private Option<string> Text(Command command, string name, string description, bool required = false, bool recursive = false)
    {
        var option = new Option<string>("--" + name) { Description = description, Required = required, Recursive = recursive };
        command.Options.Add(option); _options.Add(option, parse => parse.GetValue(option));
        return option;
    }
    private void Number(Command command, string name, string description, bool recursive = false)
    {
        var option = new Option<int>("--" + name) { Description = description, Recursive = recursive };
        option.Validators.Add(result => { if (result.GetValueOrDefault<int>() is < 1 or > 86400) result.AddError($"--{name} must be between 1 and 86400."); });
        command.Options.Add(option); _options.Add(option, parse => parse.GetValue(option).ToString(CultureInfo.InvariantCulture));
    }
    private void Word(Command command, string name, string description)
    {
        var argument = new Argument<string>(name) { Description = description };
        command.Arguments.Add(argument); _arguments.Add(argument, parse => [parse.GetValue(argument)!]);
    }
    private void Id(Command command, string name)
    {
        var argument = new Argument<Guid>(name) { Description = "UUID reported by the corresponding list or progress command" };
        command.Arguments.Add(argument); _arguments.Add(argument, parse => [parse.GetValue(argument).ToString()]);
    }
    private void Items(Command command)
    {
        var paths = new Argument<string[]>("paths") { Description = "Files or folders; relative to the caller's working directory", Arity = ArgumentArity.ZeroOrMore };
        var text = Text(command, "text", "Send literal text; use '-' to read redirected stdin");
        command.Arguments.Add(paths); _arguments.Add(paths, parse => parse.GetValue(paths) ?? []);
        command.Validators.Add(result =>
        {
            var count = result.GetValue(paths)?.Length ?? 0;
            if (count == 0 && result.GetValue(text) is null) result.AddError("Provide paths or --text TEXT.");
            if (count > 0 && result.GetValue(text) is not null) result.AddError("Do not combine paths with --text.");
        });
    }
    private void Confirm(Command command)
    {
        var yes = Flag(command, "yes", "Confirm this action");
        command.Validators.Add(result => { if (!result.GetValue(yes)) result.AddError("This action requires --yes."); });
    }
    private void Destination(Command command)
    { Text(command, "directory", "Receive destination; overrides the saved directory for this invocation"); Flag(command, "no-checksum", "Disable checksum verification for this transfer"); }
    private void WebOptions(Command command)
    { Flag(command, "auto-accept", "Approve browser requests automatically"); Text(command, "pin", "Protect browser sharing with a PIN"); }
}
