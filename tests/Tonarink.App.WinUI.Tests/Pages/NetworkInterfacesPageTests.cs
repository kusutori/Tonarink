using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Pages.Settings;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class NetworkInterfacesPageTests
{
    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SwitchingFilterModeMovesRulesWithoutReplacingCheckboxesOrLosingOtherSettings(bool allowlist)
    {
        string[] patterns = ["10.0.*.*", "192.168.1.*"];
        await using var page = new PageFixture(AppSettings.Default with
        {
            LanguageIndex = 2,
            Alias = "isolated network page",
            MinimizeToTray = true,
            NetworkWhitelist = allowlist ? patterns : null,
            NetworkBlacklist = allowlist ? null : patterns
        });
        await UntilAsync(() => page.Checkboxes.Count == 2 && page.Inputs.Count == 2);
        var selected = page.Checkboxes.Single(control => control.IsChecked == true);
        var other = page.Checkboxes.Single(control => control.IsChecked != true);
        Assert.AreSequenceEqual(patterns, page.Inputs.Select(control => control.Text));
        Assert.IsFalse(other.IsChecked);
        Assert.AreEqual(0, page.Changes.Count);

        // Native property events exercise the production page and reconciler.
        // They do not claim real mouse/keyboard input or production navigation.
        other.IsChecked = true;
        await UntilAsync(() => selected.IsChecked == false && other.IsChecked == true && page.Changes.Count == 1);
        Assert.Contains(selected, page.Checkboxes);
        Assert.Contains(other, page.Checkboxes);
        Assert.AreSequenceEqual(patterns, page.Rules);
        Assert.IsTrue(allowlist ? page.Settings.NetworkWhitelist is null : page.Settings.NetworkBlacklist is null);
        Assert.AreEqual("isolated network page", page.Settings.Alias);
        Assert.IsTrue(page.Settings.MinimizeToTray);
        Assert.AreSequenceEqual(patterns, page.Inputs.Select(control => control.Text));

        selected.IsChecked = true;
        await UntilAsync(() => selected.IsChecked == true && other.IsChecked == false && page.Changes.Count == 2);
        Assert.AreSequenceEqual(patterns, page.Rules);
        Assert.IsTrue(allowlist ? page.Settings.NetworkBlacklist is null : page.Settings.NetworkWhitelist is null);
        selected.IsChecked = false;
        await UntilAsync(() => page.Settings.NetworkWhitelist is null && page.Settings.NetworkBlacklist is null && page.Inputs.Count == 0);
        Assert.AreEqual(3, page.Changes.Count);
        Assert.IsFalse(page.Checkboxes.Any(control => control.IsChecked == true));
        Assert.IsFalse(page.Buttons.Any(control => AutomationProperties.GetName(control) == "Add"));
    }

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DraftSurvivesUnrelatedRenderAndCommitsOnFocusLossThenAddRemoveUpdatesRows(bool allowlist)
    {
        string[] patterns = ["192.168.1.*"];
        await using var page = new PageFixture(AppSettings.Default with
        {
            LanguageIndex = 2,
            Alias = "isolated draft",
            NetworkWhitelist = allowlist ? patterns : null,
            NetworkBlacklist = allowlist ? null : patterns
        });
        await UntilAsync(() => page.Inputs.Count == 1 && page.Checkboxes.Count == 2);
        var input = page.Inputs.Single();
        Assert.IsTrue(input.Focus(FocusState.Programmatic));
        await UntilAsync(() => input.FocusState != FocusState.Unfocused);
        input.Text = "10.20.*.*";
        await UntilAsync(() => page.RenderCount > 1 && input.Text == "10.20.*.*");
        Assert.AreSequenceEqual(patterns, page.Rules);
        Assert.IsEmpty(page.Changes);

        var renders = page.RenderCount;
        page.Replace(page.Settings with { MinimizeToTray = true });
        await UntilAsync(() => page.RenderCount > renders);
        Assert.AreSame(input, page.Inputs.Single());
        Assert.AreEqual("10.20.*.*", input.Text);
        Assert.IsEmpty(page.Changes, "An unrelated settings render must not commit a network draft.");
        Assert.IsTrue(page.Checkboxes.First().Focus(FocusState.Programmatic));
        await UntilAsync(() => input.FocusState == FocusState.Unfocused && page.Changes.Count == 1);
        Assert.AreSequenceEqual(new[] { "10.20.*.*" }, page.Rules);
        Assert.IsTrue(page.Settings.MinimizeToTray, "Committing a draft must preserve the latest unrelated setting.");
        Assert.AreEqual("isolated draft", page.Settings.Alias);

        Invoke(page.Buttons.Single(control => AutomationProperties.GetName(control) == "Add"));
        await UntilAsync(() => page.Inputs.Count == 2 && page.Changes.Count == 2);
        Assert.AreSequenceEqual(new[] { "10.20.*.*", "" }, page.Rules);
        Assert.AreSequenceEqual(page.Rules, page.Inputs.Select(control => control.Text));
        Invoke(page.Buttons.First(control => AutomationProperties.GetName(control) == "Remove pattern"));
        await UntilAsync(() => page.Inputs.Count == 1 && page.Changes.Count == 3 && page.Inputs[0].Text == "");
        Assert.AreSequenceEqual(new[] { "" }, page.Rules);
        Invoke(page.Buttons.Single(control => AutomationProperties.GetName(control) == "Remove pattern"));
        await UntilAsync(() => page.Inputs.Count == 0 && page.Changes.Count == 4);
        Assert.IsNull(page.Settings.NetworkWhitelist);
        Assert.IsNull(page.Settings.NetworkBlacklist);
        Assert.IsFalse(page.Checkboxes.Any(control => control.IsChecked == true));
        Assert.IsTrue(page.Settings.MinimizeToTray);
    }

    private sealed class PageFixture : IAsyncDisposable
    {
        private Action<AppSettings>? _replace;
        public ReactorHostControl Host { get; }
        public AppSettings Settings { get; private set; }
        public List<AppSettings> Changes { get; } = [];
        public int RenderCount { get; private set; }
        public IReadOnlyList<string> Rules => Settings.NetworkWhitelist ?? Settings.NetworkBlacklist ?? [];
        public List<CheckBox> Checkboxes => Descendants<CheckBox>(Host).ToList();
        public List<TextBox> Inputs => Descendants<TextBox>(Host).ToList();
        public List<Button> Buttons => Descendants<Button>(Host).ToList();

        public PageFixture(AppSettings initial)
        {
            Settings = initial;
            Host = new ReactorHostControl { OnRenderComplete = (_, _, _) => RenderCount++ };
            TestApplication.TestWindow.Content = Host;
            Host.Mount(context =>
            {
                var (settings, replace) = context.UseState(initial);
                _replace = replace;
                return LocaleProvider("en-US", Component<NetworkInterfacesPage, NetworkInterfacesPageProps>(new(settings, update =>
                {
                    Settings = update(Settings);
                    Changes.Add(Settings);
                    replace(Settings);
                })), global::Tonarink.AppShell.Resources, defaultLocale: "en-US");
            });
        }

        public void Replace(AppSettings settings)
        {
            Assert.IsNotNull(_replace);
            Settings = settings;
            _replace(settings);
        }

        public async ValueTask DisposeAsync()
        {
            try { Host.Dispose(); }
            finally { TestApplication.TestWindow.Content = new Grid(); }
            await Task.Yield();
        }
    }

    private static void Invoke(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var provider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(provider);
        provider.Invoke();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) Assert.Fail("Timed out waiting for the production network settings page.");
            await Task.Delay(25);
        }
    }
}
