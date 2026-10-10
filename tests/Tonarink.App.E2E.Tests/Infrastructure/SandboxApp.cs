using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Tonarink.TestSupport;

namespace Tonarink.App.E2E.Tests;

// A thin external driver, not another test runner or component-event shortcut.
// Every mutating command is explicitly guest-scoped. Never adopt a user's VM.
internal sealed class SandboxApp(TestContext context) : IAsyncDisposable
{
    private readonly string _winapp = Environment.GetEnvironmentVariable("TONARINK_WINAPP_EXECUTABLE") ?? "winapp";
    private readonly string _evidence = Path.Combine(context.ResultsDirectory
        ?? throw new AssertFailedException("MSTest did not supply a results directory."), "app-e2e", Guid.NewGuid().ToString("N"));
    private string? _manifest;
    private string? _hash;
    private string? _epoch;
    private JsonElement? _runtime;
    private int _processId;
    private string? _window;
    private int _command;

    public static async Task<SandboxApp> StartAsync(TestContext context)
    {
        var app = new SandboxApp(context);
        Directory.CreateDirectory(app._evidence);
        try
        {
            await app.StartCoreAsync();
            return app;
        }
        catch (Exception startError)
        {
            try { await app.DisposeAsync(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("App setup failed and cleanup could not be verified. Evidence retained.", startError, cleanupError);
            }
            throw;
        }
    }

    private async Task StartCoreAsync()
    {
        Assert.IsTrue(OperatingSystem.IsWindows(), "App E2E needs Windows; never redirect it to a CLI-only suite.");
        var layout = Path.GetFullPath(Required("TONARINK_APP_TEST_LAYOUT"));
        _hash = Required("TONARINK_APP_EXPECTED_SHA256");
        _manifest = Path.Combine(layout, "AppxManifest.xml");
        if (!File.Exists(_manifest))
            _manifest = Path.Combine(layout, "Package.appxmanifest");
        Assert.IsTrue(File.Exists(_manifest), "Pass a complete current-build layout with its actual manifest.");
        Assert.IsTrue(File.Exists(Path.Combine(layout, "resources.pri")),
            "Packaged layout requires the merged primary resources.pri; a portable publish alone is insufficient.");
        var document = XDocument.Load(_manifest);
        var identity = document.Root!.Elements().Single(element => element.Name.LocalName == "Identity");
        var application = document.Descendants().Single(element => element.Name.LocalName == "Application");
        var relativeExecutable = (string?)application.Attribute("Executable");
        Assert.AreEqual("Tonarink.exe", relativeExecutable, "Require an evaluated production App manifest, not a test host.");
        var executable = Path.Combine(layout, "Tonarink.exe");
        var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable)));
        Assert.AreEqual(_hash, actualHash, ignoreCase: true, "Artifact changed or does not belong to the selected build.");
        using (var stream = File.OpenRead(executable))
        using (var pe = new PEReader(stream))
        {
            Assert.AreEqual(PEMagic.PE32Plus, pe.PEHeaders.PEHeader!.Magic);
            Assert.AreEqual(0, pe.PEHeaders.PEHeader.CorHeaderTableDirectory.RelativeVirtualAddress,
                "This entry point verifies Native AOT, not a managed apphost.");
            var expectedMachine = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => Machine.Amd64,
                Architecture.Arm64 => Machine.Arm64,
                _ => throw new AssertFailedException("App E2E requires an x64 or ARM64 native runner."),
            };
            Assert.AreEqual(expectedMachine, pe.PEHeaders.CoffHeader.Machine);
        }

        var snapshot = await CommandAsync("target", "snapshot", "sandbox", "--json");
        Assert.IsFalse(snapshot.GetProperty("running").GetBoolean(),
            "Use a disposable runner with no existing Sandbox. Tests never adopt or stop another workflow's guest.");
        var version = await RawAsync(["--version"]);
        Assert.IsTrue(Version.TryParse(version.StandardOutput.Trim(), out var parsed) && parsed >= new Version(0, 7, 0),
            "Sandbox routing requires WinApp 0.7.0 or later.");
        Assert.IsTrue(File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsSandbox.exe")),
            "Windows Sandbox is not installed. Set it up on a disposable interactive runner; no local fallback is allowed.");

        var launched = await CommandAsync("run", layout, "--manifest", _manifest,
            "--output-appx-directory", Path.Combine(_evidence, "development-layout"),
            "--on", "sandbox", "--detach", "--json");
        Assert.IsTrue(launched.GetProperty("Sandbox").GetBoolean());
        Assert.AreEqual("sandbox", launched.GetProperty("ProcessScope").GetString());
        Assert.AreEqual("sandbox", launched.GetProperty("ExecutionTarget").GetProperty("Kind").GetString());
        _epoch = launched.GetProperty("ExecutionTarget").GetProperty("Epoch").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(_epoch));
        _processId = launched.GetProperty("ProcessId").GetInt32();
        Assert.IsGreaterThan(0, _processId);
        await CheckEpochAsync();
        _runtime = await GuestAsync($$"""
            $ErrorActionPreference = 'Stop'
            $appProcess = Get-Process -Id {{_processId}}
            $appPath = $appProcess.Path
            $appPackage = @(Get-AppxPackage | Where-Object {
                $_.InstallLocation -and $appPath.StartsWith($_.InstallLocation.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
            })
            if ($appPackage.Count -ne 1) { throw 'Cannot attribute the running executable to exactly one package.' }
            [ordered]@{
                processId = $appProcess.Id; executable = $appPath
                startTicks = $appProcess.StartTime.ToUniversalTime().Ticks.ToString()
                sha256 = (Get-FileHash -LiteralPath $appPath -Algorithm SHA256).Hash
                fullName = $appPackage[0].PackageFullName; family = $appPackage[0].PackageFamilyName
                publisher = $appPackage[0].Publisher; installLocation = $appPackage[0].InstallLocation
                version = $appPackage[0].Version.ToString(); development = $appPackage[0].IsDevelopmentMode
            } | ConvertTo-Json -Compress
            """);
        Assert.AreEqual(_hash, _runtime.Value.GetProperty("sha256").GetString(), ignoreCase: true);
        Assert.IsTrue(_runtime.Value.GetProperty("development").GetBoolean());
        Assert.AreEqual((string?)identity.Attribute("Version"), _runtime.Value.GetProperty("version").GetString());
        await SaveJsonAsync("provenance.json", JsonSerializer.SerializeToElement(new
        {
            mode = "loose-development-package", // Deliberately NOT final MSIX installation verification.
            layout,
            executable,
            actualHash,
            epoch = _epoch,
            runtime = _runtime.Value,
        }));
        var ready = await UiAsync("wait-for", "Navigation.Settings", "--timeout", "15000");
        Assert.IsTrue(ready.GetProperty("found").GetBoolean(), "The production navigation never became ready.");
        Assert.IsFalse(ready.GetProperty("timedOut").GetBoolean());
        var tree = await InspectAsync();
        var windows = tree.GetProperty("windows").EnumerateArray()
            .Where(window => Descendants(window).Any(element => Id(element) == "Navigation.Settings")).ToArray();
        Assert.HasCount(1, windows, "Find exactly one production main window in the launched PID.");
        _window = windows[0].GetProperty("hwnd").ToString();
        context.WriteLine($"Production App PID {_processId}, guest epoch {_epoch}; evidence {_evidence}.");
    }

    public async Task NavigateAsync(string page)
    {
        await CheckEpochAsync();
        await UiAsync("invoke", "Navigation." + page, "--action", "select");
        await WaitPropertyAsync("Navigation." + page, "IsSelected", "True");
    }

    public async Task ExpandAsync(string id, bool expanded)
    {
        await UiAsync("scroll-into-view", id);
        var tree = await InspectAsync();
        var groups = Descendants(tree).Where(element => Id(element) == id).ToArray();
        Assert.HasCount(1, groups, $"Expected exactly one {id}.");
        // Toolkit exposes the card as a Group, and the actual pattern on its
        // nested Expander. Discover that element; never invoke an arbitrary ancestor.
        var targets = Descendants(groups[0]).Where(element => element.TryGetProperty("expandState", out _)).ToArray();
        Assert.HasCount(1, targets, $"Expected exactly one ExpandCollapse pattern inside {id}.");
        var selector = targets[0].GetProperty("selector").GetString()!;
        await UiAsync("invoke", selector, "--action", expanded ? "expand" : "collapse");
        await WaitPropertyAsync(selector, "ExpandCollapseState", expanded ? "Expanded" : "Collapsed");
    }

    public async Task<JsonElement> UiAsync(params string[] arguments)
    {
        await CheckEpochAsync();
        return await CommandAsync(["ui", .. arguments, "--on", "sandbox", .. _window is null
            ? new[] { "--app", _processId.ToString(CultureInfo.InvariantCulture) }
            : new[] { "--window", _window }, "--json"]);
    }

    public Task<JsonElement> InspectAsync() => UiAsync("inspect", "--depth", "30");

    public async Task<string> PropertyAsync(string selector, string property)
    {
        var response = await UiAsync("get-property", selector, "--property", property);
        return response.GetProperty("properties").GetProperty(property).GetString()
            ?? throw new AssertFailedException($"UIA {selector}.{property} was unavailable.");
    }

    public async Task WaitPropertyAsync(string selector, string property, string expected)
    {
        var response = await UiAsync("wait-for", selector, "--property", property,
            "--value", expected, "--timeout", "10000");
        Assert.IsTrue(response.GetProperty("found").GetBoolean(), $"{selector}.{property} did not become {expected}.");
        Assert.IsFalse(response.GetProperty("timedOut").GetBoolean());
        Assert.AreEqual(expected, await PropertyAsync(selector, property), ignoreCase: true);
    }

    public async Task CaptureAsync(string name)
    {
        await CheckEpochAsync();
        await SaveJsonAsync(name + "-tree.json", await InspectAsync());
        var snapshot = await CommandAsync("target", "snapshot", "sandbox", "--json");
        await SaveJsonAsync(name + "-target.json", snapshot);
        // Capture is a diagnostic attachment, not a frame comparison. A failed
        // capture stays visible; it cannot turn a failed case into a passing case.
        if (snapshot.GetProperty("desktop").GetProperty("effectiveCaptureReady").GetBoolean())
        {
            await UiAsync("screenshot", "--output", Path.Combine(_evidence, name + ".png"));
            context.AddResultFile(Path.Combine(_evidence, name + ".png"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_epoch is null || _manifest is null)
            return;
        await CheckEpochAsync();
        if (_runtime is { } owned)
        {
            // Resolve every destructive target against saved runtime ownership.
            // PID reuse, changed binaries, and a recreated guest fail closed.
            var path = Quote(owned.GetProperty("executable").GetString()!);
            var ticks = Quote(owned.GetProperty("startTicks").GetString()!);
            var hash = Quote(_hash!);
            var family = Quote(owned.GetProperty("family").GetString()!);
            var logs = await GuestAsync($$"""
                $ErrorActionPreference = 'Stop'
                $logRoot = Join-Path $env:LOCALAPPDATA ('Packages\' + {{family}} + '\LocalState')
                $recentLogs = @(Get-ChildItem -LiteralPath $logRoot -Filter '*.log' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                    [ordered]@{ name = $_.Name; tail = @(Get-Content -LiteralPath $_.FullName -Tail 100) }
                })
                $appProcess = Get-Process -Id {{_processId}} -ErrorAction SilentlyContinue
                if ($appProcess) {
                    if ($appProcess.Path -ne {{path}} -or $appProcess.StartTime.ToUniversalTime().Ticks.ToString() -ne {{ticks}}) {
                        throw 'PID ownership changed; refusing cleanup.'
                    }
                    if ((Get-FileHash -LiteralPath $appProcess.Path -Algorithm SHA256).Hash -ne {{hash}}) {
                        throw 'Executable changed; refusing cleanup.'
                    }
                    Stop-Process -Id $appProcess.Id
                    $appProcess.WaitForExit(10000) | Out-Null
                    if (-not $appProcess.HasExited) { throw 'Owned process did not exit.' }
                }
                [ordered]@{ recentLogs = $recentLogs; processStopped = $true } | ConvertTo-Json -Depth 5 -Compress
                """);
            await SaveJsonAsync("cleanup-and-logs.json", logs);
        }
        // WinApp only removes this manifest's own development deployment in
        // this guest. No --clean, --force, broad name match or Sandbox stop.
        await CommandAsync("unregister", "--manifest", _manifest, "--on", "sandbox", "--json");
        context.WriteLine("Unregistered the owned guest development package. Sandbox retained; evidence saved before cleanup.");
    }

    private async Task CheckEpochAsync()
    {
        var snapshot = await CommandAsync("target", "snapshot", "sandbox", "--json");
        Assert.IsTrue(snapshot.GetProperty("running").GetBoolean());
        Assert.AreEqual(_epoch, snapshot.GetProperty("executionTarget").GetProperty("epoch").GetString(),
            "Sandbox was recreated. Never reuse a PID/window from a different generation.");
    }

    private Task<JsonElement> GuestAsync(string script) => CommandAsync("target", "exec", "sandbox", "--json", "--",
        "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand",
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

    private async Task<JsonElement> CommandAsync(params string[] arguments)
    {
        var result = await RawAsync(arguments);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind);
        return document.RootElement.Clone();
    }

    private async Task<ProcessResult> RawAsync(string[] arguments)
    {
        var sequence = ++_command;
        var timeout = arguments[0] == "run" ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(60);
        var result = await ProcessRunner.RunAsync(_winapp, arguments, _evidence, timeout);
        var path = Path.Combine(_evidence, $"command-{sequence:D3}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { arguments, result }));
        context.AddResultFile(path);
        Assert.AreEqual(0, result.ExitCode, $"WinApp failed ({string.Join(' ', arguments)}): {result.StandardOutput} {result.StandardError}");
        return result;
    }

    private async Task SaveJsonAsync(string name, JsonElement value)
    {
        var path = Path.Combine(_evidence, name);
        await File.WriteAllTextAsync(path, value.GetRawText());
        context.AddResultFile(path);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new AssertFailedException($"App E2E requires {name} for the exact current artifact; no installed fallback.");

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string? Id(JsonElement element) => element.TryGetProperty("automationId", out var id) ? id.GetString() : null;

    private static IEnumerable<JsonElement> Descendants(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                foreach (var descendant in Descendants(item))
                    yield return descendant;
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            yield return value;
            foreach (var property in value.EnumerateObject())
                if (property.Name is "windows" or "elements" or "children")
                    foreach (var descendant in Descendants(property.Value))
                        yield return descendant;
        }
    }
}
