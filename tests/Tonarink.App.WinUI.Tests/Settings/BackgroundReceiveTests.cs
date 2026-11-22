using System.Text.Json;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Cli;
using Tonarink.Hooks.Shell;
using Tonarink.Services.Settings;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
public sealed class BackgroundReceiveTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SettingDefaultsOnAndPreservesExplicitValuesThroughJsonAndCli(bool enabled)
    {
        var legacy = JsonSerializer.Deserialize("{}", AppSettingsJsonContext.Default.AppSettingsFile)!.ToSettings();
        Assert.IsTrue(legacy.ReceiveInBackground);
        var saved = CliSettings.Set(AppSettings.Default, "receive-in-background", enabled ? "true" : "false", Environment.CurrentDirectory);
        Assert.AreEqual(enabled, saved.ReceiveInBackground);
        var json = JsonSerializer.Serialize(AppSettingsFile.FromSettings(saved), AppSettingsJsonContext.Default.AppSettingsFile);
        var restored = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettingsFile)!.ToSettings();
        Assert.AreEqual(enabled, restored.ReceiveInBackground);
        Assert.IsFalse(CliSettings.Set(restored, "receive-in-background", "false", Environment.CurrentDirectory).ReceiveInBackground);
    }

    [UITestMethod]
    public async Task BackgroundRequestsDoNotRestoreAndDisablingEitherOptionRestoresThePendingRequest()
    {
        await using var fixture = new ActivationFixture(new(0, Notifications: true, Background: true));
        await fixture.ReadyAsync();
        await fixture.ReplaceAsync(fixture.Current with { Count = 1 });
        await fixture.ReplaceAsync(fixture.Current with { Count = 2 });
        Assert.AreEqual(0, fixture.Restores, "Receiving more requests must not activate a background window.");

        await fixture.ReplaceAsync(fixture.Current with { Notifications = false });
        Assert.AreEqual(1, fixture.Restores, "The background preference must not suppress the window without notifications.");
        await fixture.ReplaceAsync(fixture.Current with { Notifications = true });
        Assert.AreEqual(1, fixture.Restores);
        await fixture.ReplaceAsync(fixture.Current with { Background = false });
        Assert.AreEqual(2, fixture.Restores, "Switching off background mode must restore normal request presentation.");
        await fixture.ReplaceAsync(fixture.Current with { Epoch = 1 });
        Assert.AreEqual(2, fixture.Restores, "An unrelated render must not activate the window again.");
        await fixture.ReplaceAsync(fixture.Current with { Count = 0 });
        Assert.AreEqual(2, fixture.Restores, "Dismissal is not an activation request.");
    }

    [UITestMethod]
    public async Task ForegroundPresentationRestoresButCliOwnedRequestsDoNot()
    {
        await using var fixture = new ActivationFixture(new(1, Notifications: true, Background: false));
        await fixture.ReadyAsync();
        Assert.AreEqual(1, fixture.Restores, "Positive control: the same hook must actually invoke restore in foreground mode.");
        await fixture.ReplaceAsync(fixture.Current with { Count = 2, Cli = true });
        Assert.AreEqual(1, fixture.Restores);
        await fixture.ReplaceAsync(fixture.Current with { Cli = false });
        Assert.AreEqual(2, fixture.Restores);
    }

    private sealed record Scenario(int Count, bool Notifications, bool Background, bool Cli = false, int Epoch = 0);

    // Exercise the production effect on a real Reactor host; only the window
    // activation action is recorded instead of stealing focus during the suite.
    private sealed class ActivationFixture : IAsyncDisposable
    {
        private readonly ReactorHostControl _host = new();
        private Action<Scenario>? _replace;
        private int _commits;
        public Scenario Current { get; private set; }
        public int Restores { get; private set; }

        public ActivationFixture(Scenario initial)
        {
            Current = initial;
            TestApplication.TestWindow.Content = _host;
            _host.Mount(context =>
            {
                var (scenario, replace) = context.UseState(initial);
                _replace = replace;
                context.UseIncomingWindowActivation(scenario.Count, scenario.Notifications,
                    scenario.Background, scenario.Cli, () => Restores++);
                context.UseEffect(() => { _commits++; }, scenario);
                return TextBlock($"Pending: {scenario.Count}; render: {scenario.Epoch}");
            });
        }

        public Task ReadyAsync() => UntilAsync(() => _commits > 0);

        public async Task ReplaceAsync(Scenario scenario)
        {
            var commits = _commits;
            Current = scenario;
            _replace!(scenario);
            await UntilAsync(() => _commits > commits);
        }

        public async ValueTask DisposeAsync()
        {
            try { _host.Dispose(); }
            finally { TestApplication.TestWindow.Content = new Grid(); }
            await Task.Yield();
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) Assert.Fail("The incoming-window effect did not commit.");
            await Task.Delay(25);
        }
    }
}
