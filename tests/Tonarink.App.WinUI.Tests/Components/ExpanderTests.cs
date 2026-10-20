using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Components.Animations;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;

[assembly: DoNotParallelize]

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class ExpanderTests
{
    [UITestMethod]
    public async Task ChangingTheHeaderToggleUpdatesTheRealizedChildActionWithoutReopening()
    {
        var changes = 0;
        var clicks = 0;
        var host = new ReactorHostControl();
        TestApplication.TestWindow.Content = host;
        try
        {
            host.Mount(context =>
            {
                var (enabled, update) = context.UseState(false);
                return SettingsExpander(
                content: ToggleSwitch(enabled, value => { changes++; update(value); }).AutomationName("header toggle")
                    .AutomationId("Fixture.Enabled"),
                items: [SettingsCard(header: "child", isClickEnabled: false, isActionIconVisible: false,
                    content: AnimatedButtons.Add("action", () => clicks++, isEnabled: enabled,
                        automationId: "Fixture.Add"))])
                .AutomationId("Fixture.Expander")
                .Set(control => control.IsExpanded = true);
            });
            await UntilAsync(() => Find<SettingsExpander>(host) is not null);
            var expander = Find<SettingsExpander>(host)!;
            await UntilAsync(() => expander.Content is Microsoft.UI.Xaml.Controls.ToggleSwitch && expander.Items.Count == 1);
            var toggle = (Microsoft.UI.Xaml.Controls.ToggleSwitch)expander.Content;
            var card = (SettingsCard)expander.Items[0];
            await UntilAsync(() => Find<Microsoft.UI.Xaml.Controls.Button>(host, "Fixture.Add") is not null);
            var button = Find<Microsoft.UI.Xaml.Controls.Button>(host, "Fixture.Add")!;
            Assert.AreEqual("Fixture.Expander", AutomationProperties.GetAutomationId(expander));
            Assert.AreEqual("Fixture.Enabled", AutomationProperties.GetAutomationId(toggle));
            Assert.AreEqual("Fixture.Add", AutomationProperties.GetAutomationId(button));
            Assert.IsFalse(button.IsEnabled);
            Assert.IsFalse(toggle.IsOn);

            Assert.AreEqual(0, changes, "Initial controlled values must not be reported as user edits.");
            // Raise the native control event, not a captured Reactor state setter.
            toggle.IsOn = true;
            await UntilAsync(() => Find<Microsoft.UI.Xaml.Controls.Button>(host, "Fixture.Add")?.IsEnabled == true);
            var current = Find<Microsoft.UI.Xaml.Controls.Button>(host, "Fixture.Add")!;
            Assert.AreEqual(1, changes);
            var invoke = new ButtonAutomationPeer(current).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Assert.IsNotNull(invoke);
            invoke.Invoke();
            Assert.AreEqual(1, clicks, "The realized action must still invoke its current callback after the update.");
            Assert.AreSame(expander, Find<SettingsExpander>(host));
            Assert.AreSame(toggle, expander.Content);
            Assert.AreSame(card, expander.Items[0]);
            Assert.AreSame(button, Find<Microsoft.UI.Xaml.Controls.Button>((DependencyObject)card.Content));
            Assert.AreEqual("Fixture.Expander", AutomationProperties.GetAutomationId(expander));
            Assert.AreEqual("Fixture.Enabled", AutomationProperties.GetAutomationId(toggle));
            Assert.AreEqual("Fixture.Add", AutomationProperties.GetAutomationId(button));
            toggle.IsOn = false;
            await UntilAsync(() => Find<Microsoft.UI.Xaml.Controls.Button>(host, "Fixture.Add")?.IsEnabled == false);
            Assert.AreEqual(2, changes);
        }
        finally
        {
            // Unloaded can mean temporary reparenting; the owning fixture must
            // explicitly dispose the Reactor tree at terminal teardown.
            try { host.Dispose(); }
            finally { TestApplication.TestWindow.Content = new Grid(); }
            await Task.Yield();
        }
    }

    private static T? Find<T>(DependencyObject root, string? id = null) where T : DependencyObject
    {
        if (root is T found && (id is null || AutomationProperties.GetAutomationId(root) == id))
            return found;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(root, index), id) is { } child)
                return child;
        }
        return null;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                Assert.Fail("Timed out waiting for the production WinUI visual tree to update.");
            await Task.Delay(25);
        }
    }
}
