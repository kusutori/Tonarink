using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Application;
using Tonarink.Components.Shell;
using Tonarink.Hooks.LocalSendNode;
using Tonarink.Pages.Settings;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class SettingsPageBehaviorTests
{
    [UITestMethod]
    public async Task DeviceNameRemainsADraftUntilSaveAndStartAndPreservesOtherSettings()
    {
        await using var page = new PageFixture();
        await UntilAsync(() => page.NameInput is not null);
        page.NameInput!.Text = "  My laptop  ";
        await UntilAsync(() => page.Button("Save and start server") is not null);
        Assert.AreEqual("Original laptop", page.Settings.Alias);
        Assert.AreEqual(0, page.ApplyCalls);
        Assert.AreEqual(0, page.Updates);

        page.Replace(page.Settings with { MinimizeToTray = true });
        await UntilAsync(() => page.NameInput?.Text == "  My laptop  " && page.RenderedSettings.MinimizeToTray);
        var button = page.Button("Save and start server")!;
        var provider = new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(provider);
        provider.Invoke();
        await UntilAsync(() => page.ApplyCalls == 1 && page.NameInput?.Text == "My laptop");
        Assert.AreEqual("My laptop", page.Settings.Alias);
        Assert.IsTrue(page.Settings.MinimizeToTray);
        Assert.AreEqual(0, page.Updates, "The input must use the explicit save/start action, not an immediate setting update.");
        await UntilAsync(() => page.Button("Start server") is not null);
    }

    [UITestMethod]
    public async Task OpeningAlternatingSettingsCombosDoesNotAccumulateOptionsAfterUpdates()
    {
        await using var page = new PageFixture();
        await UntilAsync(() => page.Combo("Theme") is not null && page.Combo("Language") is not null);
        for (var round = 0; round < 4; round++)
        {
            var theme = page.Combo("Theme")!;
            await OpenComboAsync(theme);
            Assert.HasCount(3, page.Combo("Theme")!.Items);
            theme.SelectedIndex = round % 2 + 1;
            theme.IsDropDownOpen = false;
            await UntilAsync(() => page.RenderedSettings.ThemeIndex == round % 2 + 1);

            var language = page.Combo("Language")!;
            await OpenComboAsync(language);
            Assert.HasCount(AppLanguages.Choices.Count, page.Combo("Language")!.Items);
            language.IsDropDownOpen = false;
            Assert.HasCount(3, page.Combo("Theme")!.Items);
        }
        Assert.AreEqual(0, page.ApplyCalls);
    }

    private static async Task OpenComboAsync(ComboBox combo)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object> handler = (_, _) => opened.TrySetResult();
        combo.DropDownOpened += handler;
        try
        {
            combo.IsDropDownOpen = true;
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(combo.IsDropDownOpen, "Counting closed items must not masquerade as opening the native dropdown.");
        }
        finally { combo.DropDownOpened -= handler; }
    }

    // Actual SettingsPage, native controls and Reactor event wiring. Only the
    // persistence/server boundary is substituted; this is not packaged App E2E.
    private sealed class PageFixture : IAsyncDisposable
    {
        private Action<AppSettings>? _replace;
        private readonly ReactorHostControl _host = new();
        public AppSettings Settings { get; private set; } = AppSettings.Default with { Alias = "Original laptop", LanguageIndex = 2 };
        public AppSettings RenderedSettings { get; private set; } = AppSettings.Default;
        public int ApplyCalls { get; private set; }
        public int Updates { get; private set; }
        public TextBox? NameInput => Descendants<TextBox>(_host).SingleOrDefault(control => AutomationProperties.GetAutomationId(control) == "Settings.DeviceName");
        public Button? Button(string name) => Descendants<Button>(_host).SingleOrDefault(control => AutomationProperties.GetName(control) == name);
        public ComboBox? Combo(string name) => Descendants<ComboBox>(_host).SingleOrDefault(control => AutomationProperties.GetName(control) == name);

        public PageFixture()
        {
            TestApplication.TestWindow.Content = _host;
            var slot = new FloatingInfoBarSlot { Invalidate = () => { } };
            _host.Mount(context =>
            {
                var (settings, replace) = context.UseState(Settings);
                _replace = replace;
                RenderedSettings = settings;
                var navigation = context.UseNavigation(AppRoute.Settings);
                return LocaleProvider("en-US", NavigationHost(navigation, _ => Component<SettingsPage, SettingsPageProps>(new(
                    settings, AppRuntimeState.Initial,
                    update => { Updates++; Replace(update(Settings)); },
                    update => { ApplyCalls++; Replace(update(Settings)); },
                    () => Assert.Fail("The save/start action must not stop the server."))))
                    .Provide(FloatingInfoBarHost.Slot, slot), global::Tonarink.AppShell.Resources, defaultLocale: "en-US");
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
            foreach (var combo in Descendants<ComboBox>(_host)) combo.IsDropDownOpen = false;
            try { _host.Dispose(); }
            finally { TestApplication.TestWindow.Content = new Grid(); }
            await Task.Yield();
        }
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
            if (DateTime.UtcNow >= deadline) Assert.Fail("Timed out waiting for the production SettingsPage.");
            await Task.Delay(25);
        }
    }
}
