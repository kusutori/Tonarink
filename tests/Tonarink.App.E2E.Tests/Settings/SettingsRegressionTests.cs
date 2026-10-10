using System.Globalization;

namespace Tonarink.App.E2E.Tests;

[TestClass]
[TestCategory("E2E")]
[TestCategory("Packaged")]
public sealed class SettingsRegressionTests
{
    private static SandboxApp? _app;
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context) => _app = await SandboxApp.StartAsync(context);

    [ClassCleanup]
    public static async Task StopAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [TestCleanup]
    public async Task CaptureAsync()
    {
        if (_app is not null)
            await _app.CaptureAsync(TestContext.TestName!);
    }

    [TestMethod]
    public async Task ThreeNavigationRoundTripsKeepSettingsDraftAndScrollPosition()
    {
        var app = _app!;
        await app.NavigateAsync("Settings");
        const string input = "Settings.DeviceName";
        var original = await app.PropertyAsync(input, "Value");
        var draft = "navigation-draft-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await app.UiAsync("scroll-into-view", input);
            await app.UiAsync("set-value", input, draft);
            await app.WaitPropertyAsync(input, "Value", draft);
            await app.UiAsync("scroll", "Settings.Page", "--to", "bottom");
            var offset = double.Parse(await app.PropertyAsync("Settings.Page", "ScrollVerticalPercent"), CultureInfo.InvariantCulture);
            Assert.IsGreaterThan(0d, offset, "The test must actually move away from the initial page position.");
            for (var round = 0; round < 3; round++)
            {
                await app.NavigateAsync(round % 2 == 0 ? "Receive" : "Send");
                await app.NavigateAsync("Settings");
                Assert.AreEqual(draft, await app.PropertyAsync(input, "Value"), $"Draft changed on round {round + 1}.");
                var restored = double.Parse(await app.PropertyAsync("Settings.Page", "ScrollVerticalPercent"), CultureInfo.InvariantCulture);
                Assert.AreEqual(offset, restored, 0.5d, $"Scroll reset on round {round + 1}.");
            }
        }
        finally
        {
            await app.NavigateAsync("Settings");
            await app.UiAsync("set-value", input, original);
        }
    }

    [TestMethod]
    public async Task AllExpandersOpenAndNotificationChildrenTrackToggleWithoutNavigating()
    {
        var app = _app!;
        await app.NavigateAsync("Settings");
        foreach (var group in new[] { "Tray", "Notifications", "Pin", "Advanced", "Preview", "About" })
        {
            await app.ExpandAsync($"Settings.{group}.Expander", expanded: true);
            await app.ExpandAsync($"Settings.{group}.Expander", expanded: false);
        }
        await app.ExpandAsync("Settings.Notifications.Expander", expanded: true);
        const string toggle = "Settings.Notifications.Enabled";
        var original = await app.PropertyAsync(toggle, "ToggleState");
        try
        {
            foreach (var enabled in new[] { false, true, false })
            {
                await app.UiAsync("invoke", toggle, "--action", enabled ? "toggle-on" : "toggle-off");
                await app.WaitPropertyAsync(toggle, "ToggleState", enabled ? "On" : "Off");
                await app.WaitPropertyAsync("Settings.Notifications.Action", "IsEnabled", enabled.ToString());
                await app.WaitPropertyAsync("Settings.Notifications.Test", "IsEnabled", enabled.ToString());
            }
        }
        finally
        {
            await app.UiAsync("invoke", toggle, "--action", original == "On" ? "toggle-on" : "toggle-off");
        }
    }
}
