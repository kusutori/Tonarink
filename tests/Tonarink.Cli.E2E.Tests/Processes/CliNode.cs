using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tonarink.TestSupport;

namespace Tonarink.Cli.E2E.Tests;

internal sealed record CliInvocation(ProcessResult Process, JsonElement[] Messages)
{
    public JsonElement Result => Messages.Last();
    public JsonElement Data => Result.GetProperty("data");
}

// Owns only its unique profile; never targets an installed app or default port.
internal sealed class CliNode : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;
    private readonly string _executable;
    public string Profile => _directory.Path;
    public string Downloads => Path.Combine(Profile, "received");
    public int Port { get; private set; }
    public string Target => $"https://127.0.0.1:{Port}";

    private CliNode()
    {
        _executable = Path.GetFullPath(Environment.GetEnvironmentVariable("TONARINK_CLI_TEST_EXECUTABLE")
            ?? throw new InvalidOperationException("TONARINK_CLI_TEST_EXECUTABLE must name this build's CLI artifact."));
        if (!File.Exists(_executable))
            throw new FileNotFoundException("CLI artifact not found", _executable);
        _directory = new TemporaryDirectory();
    }

    public static async Task<CliNode> StartAsync(string name)
    {
        var node = new CliNode();
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            node.Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            await node.SuccessAsync("settings", "set", "alias", name);
            await node.SuccessAsync("settings", "set", "download-directory", node.Downloads);
            await node.SuccessAsync("settings", "set", "port", node.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await node.SuccessAsync("server", "start");
            return node;
        }
        catch
        {
            await node.DisposeAsync();
            throw;
        }
    }

    public async Task<CliInvocation> RunAsync(string[] arguments, string? input = null, string? workingDirectory = null, int timeoutSeconds = 20,
        Action<JsonElement>? output = null)
    {
        var process = await ProcessRunner.RunAsync(_executable,
            ["--profile", Profile, "--json", "--language", "en-US", "--timeout", timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), .. arguments],
            workingDirectory ?? Profile, TimeSpan.FromSeconds(30), input,
            output is null ? null : line => output(ParseMessage(line)));
        var messages = process.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseMessage).ToArray();
        Assert.IsNotEmpty(messages, process.StandardError);
        Assert.AreEqual("result", messages[^1].GetProperty("type").GetString());
        Assert.AreEqual(process.ExitCode, messages[^1].GetProperty("exitCode").GetInt32());
        Assert.IsEmpty(process.StandardError);
        return new(process, messages);
    }

    private static JsonElement ParseMessage(string line)
    {
        using var parsed = JsonDocument.Parse(line);
        return parsed.RootElement.Clone();
    }

    public Task<ProcessResult> RunTextOutputAsync(string[] arguments, string language = "en-US") =>
        ProcessRunner.RunAsync(_executable,
            ["--profile", Profile, "--language", language, "--timeout", "20", .. arguments],
            Profile, TimeSpan.FromSeconds(30));

    public async Task<CliInvocation> SuccessAsync(params string[] arguments)
    {
        var invocation = await RunAsync(arguments);
        Assert.AreEqual(0, invocation.Process.ExitCode, invocation.Process.StandardOutput);
        return invocation;
    }

    public async Task<JsonElement> WaitForIncomingAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            var incoming = (await SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray();
            if (incoming.Length > 0)
                return Assert.ContainsSingle(incoming);
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        throw new AssertFailedException("CLI receiver did not report the pending transfer.");
    }

    public async Task WaitForNoIncomingAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            if (!(await SuccessAsync("receive", "list")).Data.EnumerateArray().Any()) return;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        throw new AssertFailedException("CLI receiver retained a completed or disconnected incoming request.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await SuccessAsync("app", "quit", "--yes");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                try
                {
                    var file = Path.Combine(Profile, "host.lock");
                    if (File.Exists(file))
                    {
                        using var ownership = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    break;
                }
                catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(50); }
            }
        }
        catch
        {
            // Do not erase diagnostics or delete a profile still owned by a
            // host when quit/ownership verification has failed.
            Console.WriteLine($"CLI cleanup failed; retained owned test profile: {Profile}");
            throw;
        }
        await _directory.DisposeAsync();
    }
}
