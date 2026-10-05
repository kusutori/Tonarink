using System.CommandLine;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace Tonarink.Cli;

sealed record CliClientContext(string DataDirectory, string ExecutablePath, bool Integrated);

static class CliClient
{
    public static bool ShouldRun(string[] arguments)
    {
        if (IsExecutionAliasInvocation()) return true;
        if (arguments.Length == 0) return CliConsole.HasConsole;
        if (arguments[0] == "--cli") return true;
        var root = new CliCommandLine(integrated: true).Root;
        return root.Subcommands.Any(command => command.Name == arguments[0] || command.Aliases.Contains(arguments[0]))
            || root.Options.Any(option => option.Name == arguments[0] || option.Aliases.Contains(arguments[0]));
    }

    private static bool IsExecutionAliasInvocation()
    {
        if (!OperatingSystem.IsWindows()) return false;
        // Full-trust execution aliases preserve the alias in argv[0], but do
        // not prepend the extension's uap10:Parameters to the native command
        // line. Recognize the Windows-managed alias directory so even no args
        // and unknown commands receive CLI help/errors instead of opening GUI.
        var aliasRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps") + Path.DirectorySeparatorChar;
        var invocation = Environment.GetCommandLineArgs()[0];
        if (Path.GetFullPath(invocation).StartsWith(aliasRoot, StringComparison.OrdinalIgnoreCase)) return true;
        // cmd.exe preserves an unqualified argv[0], unlike PowerShell. Resolve
        // that name against the caller's current directory and PATH, without
        // relying on the alias having a different filename from Tonarink.exe.
        if (invocation.Contains(Path.DirectorySeparatorChar) || invocation.Contains(Path.AltDirectorySeparatorChar)) return false;
        var file = Path.HasExtension(invocation) ? invocation : invocation + ".exe";
        if (File.Exists(Path.Combine(Environment.CurrentDirectory, file))) return false;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var value = directory.Trim().Trim('"');
            if (value.Length == 0) continue;
            var candidate = Path.GetFullPath(Path.Combine(Environment.ExpandEnvironmentVariables(value), file));
            if (File.Exists(candidate)) return candidate.StartsWith(aliasRoot, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static async Task<int> RunAsync(string[] arguments, CliClientContext context)
    {
        CliConsole.Initialize();
        if (arguments.FirstOrDefault() == "--cli") arguments = arguments[1..];
        if (arguments.Length == 0) arguments = ["--help"];
        // Use the same typed option to select culture before constructing help
        // descriptions and library diagnostics; no separate argument scanner.
        var bootstrap = new CliCommandLine(context.Integrated);
        using var language = CliText.UseLanguage(bootstrap.Root.Parse(arguments).GetValue(bootstrap.Language));
        CliCommandLine? commandLine = null;
        commandLine = new(context.Integrated, (parse, token) => ExecuteAsync(arguments, parse, commandLine!, context, token));
        var parsed = commandLine.Root.Parse(arguments);
        var json = parsed.GetValue(commandLine.Json);
        if (parsed.Errors.Count > 0)
        {
            if (json) Print(CliProtocol.Error(string.Join('\n', parsed.Errors.Select(e => e.Message)), 2), true);
            else
            {
                using var usage = new StringWriter();
                using var diagnostics = new StringWriter();
                await parsed.InvokeAsync(new InvocationConfiguration { Output = usage, Error = diagnostics }).ConfigureAwait(false);
                if (diagnostics.GetStringBuilder().Length > 0) Console.Error.Write(CliText.LibraryHelp(diagnostics.ToString()));
                if (usage.GetStringBuilder().Length > 0) Console.Out.Write(CliText.LibraryHelp(usage.ToString()));
            } // library-owned usage and diagnostics
            return 2;
        }
        var configuration = new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(10) };
        // Capture only the library's own help/version output when JSON is requested.
        // Business commands already emit NDJSON through Print, not this writer.
        using var generated = new StringWriter();
        var libraryAction = !commandLine.IsBusinessInvocation(parsed) || parsed.Action != parsed.CommandResult.Command.Action;
        if (libraryAction) configuration.Output = generated;
        var exit = await parsed.InvokeAsync(configuration).ConfigureAwait(false);
        if (generated.GetStringBuilder().Length > 0)
            Print(CliProtocol.Success(CliText.LibraryHelp(generated.ToString().TrimEnd())), json);
        return exit;
    }

    private static async Task<int> ExecuteAsync(string[] arguments, ParseResult parsed, CliCommandLine commandLine,
        CliClientContext context, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var json = parsed.GetValue(commandLine.Json);
        try
        {
            if (!context.Integrated && parsed.GetValue(commandLine.Profile) is { } directory)
                context = context with { DataDirectory = CliPath.FullPath(directory, Environment.CurrentDirectory) };
            if (parsed.GetValue(commandLine.Timeout) is { } timeout) cancellation.CancelAfter(TimeSpan.FromSeconds(timeout));
            string? input = null;
            if (commandLine.ReadsStandardInput(parsed))
            {
                if (!Console.IsInputRedirected) throw new CliException(CliText.Get("--text - requires redirected standard input."));
                var buffer = new char[1024 * 1024 + 1]; var length = 0;
                while (length < buffer.Length)
                {
                    var read = await Console.In.ReadAsync(buffer.AsMemory(length), cancellation.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    length += read;
                }
                if (length == buffer.Length) throw new CliException(CliText.Get("Stdin text must not exceed 1 MiB of characters."));
                input = new string(buffer, 0, length);
            }
            using var pipe = await ConnectAsync(parsed, commandLine, context, cancellation.Token).ConfigureAwait(false);
            if (pipe is null)
            {
                Print(commandLine.IsStatus(parsed)
                    ? CliProtocol.Result(CliText.Get("Host is not running"), new CliStatus(false, "Stopped", null, null, null, null, null, null), CliJsonContext.Default.CliStatus)
                    : CliProtocol.Success(CliText.Get("Host is already stopped")), json);
                return 0;
            }
            await CliProtocol.WriteAsync(pipe, new CliRequest(arguments, Environment.CurrentDirectory, input, System.Globalization.CultureInfo.CurrentUICulture.Name),
                CliJsonContext.Default.CliRequest, cancellation.Token).ConfigureAwait(false);
            while (true)
            {
                var response = await CliProtocol.ReadAsync(pipe, CliJsonContext.Default.CliResponse, cancellation.Token).ConfigureAwait(false);
                Print(response, json);
                if (response.Type == "result") return response.ExitCode;
            }
        }
        catch (OperationCanceledException) { Print(CliProtocol.Error(CliText.Get("Command cancelled or timed out"), 130), json); return 130; }
        catch (CliException exception) { Print(CliProtocol.Error(exception.Message, exception.ExitCode), json); return exception.ExitCode; }
        catch (Exception exception) { Print(CliProtocol.Error(exception.Message), json); return 1; }
    }

    private static async Task<NamedPipeClientStream?> ConnectAsync(ParseResult parsed, CliCommandLine commandLine,
        CliClientContext context, CancellationToken token)
    {
        var pipe = NewPipe();
        try { await pipe.ConnectAsync(750, token).ConfigureAwait(false); return pipe; }
        catch (TimeoutException) { pipe.Dispose(); }
        if (commandLine.CanRunWithoutHost(parsed)) return null;
        if (parsed.GetValue(commandLine.NoStart)) throw new CliException(CliText.Get("The host is not running; remove --no-start to start it."), 3);
        if (context.Integrated)
        {
            // Shell launch detaches from redirected CLI stdio. The existing
            // packaged primary-instance broker serializes concurrent startups.
            using var startup = Process.Start(new ProcessStartInfo(context.ExecutablePath)
            { UseShellExecute = true, Arguments = CliProtocol.BackgroundArgument, WindowStyle = ProcessWindowStyle.Hidden })
                ?? throw new CliException(CliText.Get("Could not start the background app."), 3);
        }
        else
        {
            var start = new ProcessStartInfo(context.ExecutablePath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            // dotnet run/framework-dependent DLL launches must restart this
            // assembly, not accidentally run the dotnet driver without a DLL.
            if (Path.GetFileNameWithoutExtension(context.ExecutablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Tonarink.Cli.dll"));
            start.ArgumentList.Add("--cli-daemon"); start.ArgumentList.Add(context.DataDirectory);
            using var startup = Process.Start(start) ?? throw new CliException(CliText.Get("Could not start the independent host."), 3);
            // The headless host writes diagnostics to its profile's log, never
            // these inherited pipe handles; it also never reads console input.
        }
        pipe = NewPipe();
        try { await pipe.ConnectAsync(45000, token).ConfigureAwait(false); return pipe; }
        catch { pipe.Dispose(); throw; }
        NamedPipeClientStream NewPipe() => new(".", CliProtocol.PipeName(context.DataDirectory), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    private static void Print(CliResponse response, bool json)
    {
        if (json) Console.Out.WriteLine(JsonSerializer.Serialize(response, CliJsonContext.Default.CliResponse));
        else if (response.Type == "progress" || response.ExitCode != 0) Console.Error.WriteLine(response.Message);
        else Console.Out.WriteLine(response.Message);
    }
}
