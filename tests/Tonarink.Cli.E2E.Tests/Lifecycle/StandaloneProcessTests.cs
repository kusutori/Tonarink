using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.TestSupport;

[assembly: DoNotParallelize]

namespace Tonarink.Cli.E2E.Tests;

[TestClass]
[TestCategory("E2E")]
public sealed class StandaloneProcessTests
{
    private TemporaryDirectory _directory = null!;
    private string _executable = null!;

    [TestInitialize]
    public void Initialize()
    {
        _executable = Environment.GetEnvironmentVariable("TONARINK_CLI_TEST_EXECUTABLE")
            ?? throw new InvalidOperationException("Set TONARINK_CLI_TEST_EXECUTABLE to this build's standalone CLI executable. Never use an installed/cached release.");
        Assert.IsTrue(File.Exists(_executable), $"CLI artifact does not exist: {_executable}");
        _executable = Path.GetFullPath(_executable);
        _directory = new();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_directory is null)
            return;
        // Every invocation uses the fixture-owned profile, including cleanup.
        var quit = await RunAsync("app", "quit", "--yes");
        Assert.AreEqual(0, quit.ExitCode, quit.StandardError);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            try
            {
                var file = Path.Combine(_directory.Path, "host.lock");
                if (File.Exists(file))
                {
                    using var ownership = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                break;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
            catch (IOException)
            {
                Console.WriteLine($"Owned profile cleanup timed out after {elapsed.Elapsed}: {_directory.Path}; quit output: {quit.StandardOutput}");
                var status = await RunAsync("--json", "--no-start", "status");
                Console.WriteLine($"Status after failed lock release: {status.StandardOutput}; {status.StandardError}");
                var log = Path.Combine(_directory.Path, "cli.log");
                if (File.Exists(log)) Console.WriteLine(await File.ReadAllTextAsync(log));
                throw;
            }
        }
        Console.WriteLine($"Owned profile released after quit in {elapsed.Elapsed}.");
        await _directory.DisposeAsync();
    }

    [TestMethod]
    [DataRow("--help")]
    [DataRow("status")]
    public async Task ReadOnlyEntry_DoesNotCreateProfileOrStartHost(string command)
    {
        var result = await RunAsync(command);
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.IsTrue(result.StandardOutput.Length > 0);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_directory.Path));
    }

    [TestMethod]
    public async Task Version_WithoutBusinessOptions_ExitsSuccessfully()
    {
        var result = await ProcessRunner.RunAsync(_executable, ["--version"], _directory.Path, TimeSpan.FromSeconds(15));
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.IsTrue(result.StandardOutput.Length > 0);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_directory.Path));
    }

    [TestMethod]
    public async Task SyntaxErrors_ReportExternalExitCodeTwoAndNoHost()
    {
        var result = await RunAsync("send", "--text", "hello");
        Assert.AreEqual(2, result.ExitCode);
        Assert.IsTrue(result.StandardError.Length > 0);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_directory.Path));
        var json = await RunAsync("--json", "send", "--text", "hello");
        Assert.AreEqual(2, json.ExitCode);
        Assert.AreEqual(2, Result(json).GetProperty("exitCode").GetInt32());
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_directory.Path));
    }

    [TestMethod]
    public async Task NoStart_WithNoHost_ReturnsUnavailableWithoutCreatingData()
    {
        var result = await RunAsync("--json", "--no-start", "settings", "list");
        Assert.AreEqual(3, result.ExitCode);
        Assert.AreEqual(3, Result(result).GetProperty("exitCode").GetInt32());
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_directory.Path));
    }

    [TestMethod]
    public async Task AutoStartedHost_PersistsSettingsAndKeepsProfilesIsolated()
    {
        var saved = await RunAsync("--json", "settings", "set", "alias", "测试 device");
        Assert.AreEqual(0, saved.ExitCode, saved.StandardOutput + saved.StandardError);
        var read = Result(await RunAsync("--json", "settings", "get", "alias"));
        Assert.AreEqual("测试 device", read.GetProperty("data")[0].GetProperty("value").GetString());
        using var other = new TemporaryDirectory();
        var status = await ProcessRunner.RunAsync(_executable, ["--profile", other.Path, "--json", "status"], other.Path, TimeSpan.FromSeconds(15));
        Assert.IsFalse(Result(status).GetProperty("data").GetProperty("appRunning").GetBoolean());
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(other.Path));
        Assert.AreEqual(0, (await RunAsync("app", "quit", "--yes")).ExitCode);
        // Reconnecting starts a new host from the same settings, not defaults.
        read = Result(await RunAsync("--json", "settings", "get", "alias"));
        Assert.AreEqual("测试 device", read.GetProperty("data")[0].GetProperty("value").GetString());
    }

    [TestMethod, Timeout(60_000)]
    public async Task ConcurrentFirstCommandsReuseOneOwnerAndRejectACompetingDaemon()
    {
        var values = Enumerable.Range(0, 6).Select(index => $"concurrent-{index}-测试").ToArray();
        var calls = values.Select(value => RunAsync("--json", "settings", "set", "alias", value)).ToArray();
        foreach (var result in await Task.WhenAll(calls))
            Assert.AreEqual(0, Result(result).GetProperty("exitCode").GetInt32(), result.StandardOutput + result.StandardError);
        Assert.IsTrue(Result(await RunAsync("--json", "status")).GetProperty("data").GetProperty("appRunning").GetBoolean());
        var saved = Result(await RunAsync("--json", "settings", "get", "alias")).GetProperty("data")[0].GetProperty("value").GetString();
        Assert.Contains(saved!, values);
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory.Path, "settings.json")));
        Assert.AreEqual(saved, persisted.RootElement.GetProperty("alias").GetString());
        // The existing owner must retain its lock; an explicit losing child
        // exits instead of starting another listener for this same profile.
        await using var contender = new OwnedDaemon(_executable, _directory.Path);
        Assert.AreEqual(0, await contender.WaitForExitAsync());
        Assert.AreEqual(saved, Result(await RunAsync("--json", "--no-start", "settings", "get", "alias"))
            .GetProperty("data")[0].GetProperty("value").GetString());
    }

    [TestMethod, Timeout(60_000)]
    public async Task KilledOwnedHostReleasesItsPortAndAutoStartRestoresSettingsAndIdentity()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Assert.AreEqual(0, (await RunAsync("settings", "set", "alias", "crash recovery 测试")).ExitCode);
        Assert.AreEqual(0, (await RunAsync("settings", "set", "port", port.ToString(System.Globalization.CultureInfo.InvariantCulture))).ExitCode);
        Assert.AreEqual(0, (await RunAsync("app", "quit", "--yes")).ExitCode);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                using var ownership = new FileStream(Path.Combine(_directory.Path, "host.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(50); }
        }
        await using var daemon = new OwnedDaemon(_executable, _directory.Path);
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (!Result(await RunAsync("--json", "status")).GetProperty("data").GetProperty("appRunning").GetBoolean())
        {
            Assert.IsFalse(daemon.HasExited, "The explicit owned daemon must become the profile owner.");
            Assert.IsTrue(DateTime.UtcNow < deadline, "Owned daemon never opened its listener.");
            await Task.Delay(50);
        }
        Assert.AreEqual(0, (await RunAsync("server", "start")).ExitCode);
        var fingerprint = Result(await RunAsync("--json", "status")).GetProperty("data").GetProperty("fingerprint").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint));
        await daemon.KillAsync();
        Assert.IsFalse(Result(await RunAsync("--json", "status")).GetProperty("data").GetProperty("appRunning").GetBoolean());
        Assert.AreEqual(3, (await RunAsync("--json", "--no-start", "settings", "list")).ExitCode);
        using (var ownership = new TcpListener(IPAddress.IPv6Any, port))
        {
            ownership.Server.DualMode = true;
            ownership.Server.ExclusiveAddressUse = true;
            ownership.Start();
        }
        Assert.AreEqual("crash recovery 测试", Result(await RunAsync("--json", "settings", "get", "alias"))
            .GetProperty("data")[0].GetProperty("value").GetString());
        Assert.AreEqual(0, (await RunAsync("server", "start")).ExitCode);
        var recovered = Result(await RunAsync("--json", "status")).GetProperty("data");
        Assert.AreEqual("Running", recovered.GetProperty("state").GetString());
        Assert.AreEqual(port, recovered.GetProperty("port").GetInt32());
        Assert.AreEqual(fingerprint, recovered.GetProperty("fingerprint").GetString());
    }

    [TestMethod]
    public async Task ServerStop_ReleasesPortButKeepsHostUsable()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Assert.AreEqual(0, (await RunAsync("settings", "set", "port", port.ToString(System.Globalization.CultureInfo.InvariantCulture))).ExitCode);
        var started = await RunAsync("server", "start");
        Assert.AreEqual(0, started.ExitCode, started.StandardOutput + started.StandardError);
        var running = Result(await RunAsync("--json", "status")).GetProperty("data");
        Assert.AreEqual("Running", running.GetProperty("state").GetString());
        var fingerprint = running.GetProperty("fingerprint").GetString();
        Assert.AreEqual(0, (await RunAsync("server", "stop")).ExitCode);
        var stopped = Result(await RunAsync("--json", "status")).GetProperty("data");
        Assert.AreEqual("Stopped", stopped.GetProperty("state").GetString());
        Assert.IsTrue(stopped.GetProperty("appRunning").GetBoolean());
        var ownership = new TcpListener(IPAddress.IPv6Any, port);
        ownership.Server.DualMode = true;
        ownership.Server.ExclusiveAddressUse = true;
        try { ownership.Start(); }
        finally { ownership.Stop(); }
        Assert.AreEqual(0, (await RunAsync("server", "restart")).ExitCode);
        Assert.AreEqual(fingerprint, Result(await RunAsync("--json", "status")).GetProperty("data").GetProperty("fingerprint").GetString());
    }

    private Task<ProcessResult> RunAsync(params string[] arguments) => ProcessRunner.RunAsync(_executable,
        ["--profile", _directory.Path, "--timeout", "15", .. arguments], _directory.Path, TimeSpan.FromSeconds(30));

    private static JsonElement Result(ProcessResult process)
    {
        var lines = process.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.IsNotEmpty(lines, process.StandardError);
        var responses = lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();
        Assert.AreEqual("result", responses[^1].GetProperty("type").GetString());
        Assert.AreEqual(process.ExitCode, responses[^1].GetProperty("exitCode").GetInt32());
        return responses[^1];
    }
}
