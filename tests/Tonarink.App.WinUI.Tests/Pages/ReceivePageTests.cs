using CommunityToolkit.WinUI.Controls;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Components.Shell;
using Tonarink.Hooks.LocalSendNode;
using Tonarink.Pages.Receive;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class ReceivePageTests
{
    [UITestMethod]
    [DataRow(LocalSendNodeState.Created)]
    [DataRow(LocalSendNodeState.Starting)]
    [DataRow(LocalSendNodeState.Running)]
    [DataRow(LocalSendNodeState.Stopping)]
    [DataRow(LocalSendNodeState.Stopped)]
    [DataRow(LocalSendNodeState.Faulted)]
    [DataRow(LocalSendNodeState.Disposed)]
    public async Task WebReceiveEntryTracksTheActualNodeState(LocalSendNodeState state)
    {
        await using var page = new PageFixture(AppSettings.Default with { Alias = "Owned receiver" }, AppRuntimeState.Initial with { NodeState = state });
        await UntilAsync(() => page.WebReceiveButton is not null && page.Texts.Contains("Owned receiver"));
        var button = page.WebReceiveButton!;
        Assert.AreEqual(state == LocalSendNodeState.Running, button.IsEnabled);
        Assert.Contains("Loading identity…", page.Texts);
        Assert.IsEmpty(page.Changes);
        var rendered = page.RenderCount;
        page.ReplaceRuntime(page.Runtime with { NodeState = LocalSendNodeState.Running, AppliedReceivePin = "5678" });
        await UntilAsync(() => page.RenderCount > rendered && button.IsEnabled);
        Assert.AreSame(button, page.WebReceiveButton);
        Invoke(button);
        await UntilAsync(() => page.Route == AppRoute.WebReceive && page.Texts.Contains("Owned web destination"));
        Assert.IsEmpty(page.Changes, "Navigation must not save receive settings.");
    }

    [UITestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task SegmentedSelectionUpdatesBothAutoSaveFieldsAndPreservesLatestSettings(int initialMode)
    {
        var initial = (AutoSaveMode)initialMode;
        await using var page = new PageFixture(AppSettings.Default with
        {
            Alias = "Owned autosave receiver", AutoSave = initial, FavoritesOnly = initial == AutoSaveMode.Favorites
        }, AppRuntimeState.Initial);
        await UntilAsync(() => page.Segments.Count == 1 && page.Segments[0].Items.Count == 3);
        var segmented = page.Segments.Single();
        Assert.AreEqual((int)initial, segmented.SelectedIndex);
        Assert.IsEmpty(page.Changes, "Applying controlled native values must not echo a user change. Received: "
            + string.Join(", ", page.Changes.Select(settings => (int)settings.AutoSave)));
        var renders = page.RenderCount;
        page.ReplaceSettings(page.Settings with { MinimizeToTray = true, ReceivePin = "5678" });
        await UntilAsync(() => page.RenderCount > renders);
        Assert.AreSame(segmented, page.Segments.Single());
        Assert.IsEmpty(page.Changes);
        foreach (var mode in new[] { AutoSaveMode.Off, AutoSaveMode.Favorites, AutoSaveMode.On, AutoSaveMode.Off }.Where(mode => mode != initial || page.Changes.Count > 0))
        {
            if (segmented.SelectedIndex == (int)mode) continue;
            var previous = page.Changes.Count;
            renders = page.RenderCount;
            // Native SelectionChanged, not the production callback invoked by
            // the test. This is control wiring, not OS mouse/keyboard coverage.
            segmented.SelectedIndex = (int)mode;
            await UntilAsync(() => page.RenderCount > renders && page.Changes.Count >= previous + 1
                && page.Settings.AutoSave == mode && segmented.SelectedIndex == (int)mode,
                () => $"Mode={mode}; native={segmented.SelectedIndex}; changes="
                    + string.Join(",", page.Changes.Select(settings => settings.AutoSave)));
            Assert.AreEqual(previous + 1, page.Changes.Count);
            Assert.AreEqual(mode == AutoSaveMode.Favorites, page.Settings.FavoritesOnly);
            Assert.AreEqual("Owned autosave receiver", page.Settings.Alias);
            Assert.AreEqual("5678", page.Settings.ReceivePin);
            Assert.IsTrue(page.Settings.MinimizeToTray);
            Assert.AreSame(segmented, page.Segments.Single());
            Assert.AreEqual((int)mode, segmented.SelectedIndex);
        }
    }

    [UITestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task EmptySelectionAndTemplateReapplicationDoNotSaveOrBlockTheNextEdit(int mode)
    {
        await using var page = new PageFixture(AppSettings.Default with
        {
            AutoSave = (AutoSaveMode)mode, FavoritesOnly = mode == (int)AutoSaveMode.Favorites
        }, AppRuntimeState.Initial);
        await UntilAsync(() => page.Segments.Count == 1 && page.Segments[0].IsLoaded);
        var segmented = page.Segments.Single();
        segmented.SelectedIndex = -1;
        Assert.IsEmpty(page.Changes, "An empty native selection is not a valid autosave mode.");
        var renders = page.RenderCount;
        page.ReplaceRuntime(page.Runtime with { NodeState = LocalSendNodeState.Running });
        await UntilAsync(() => page.RenderCount > renders && segmented.SelectedIndex == mode);
        Assert.IsEmpty(page.Changes, "Restoring a controlled selection must not save settings.");
        var template = segmented.Template;
        Assert.IsNotNull(template);
        segmented.Template = null;
        segmented.Template = template;
        segmented.ApplyTemplate();
        Assert.AreEqual(mode, segmented.SelectedIndex);
        Assert.IsEmpty(page.Changes, "Toolkit's template clear/restore pair must not save settings.");
        var nextMode = (AutoSaveMode)((mode + 1) % 3);
        renders = page.RenderCount;
        segmented.SelectedIndex = (int)nextMode;
        await UntilAsync(() => page.RenderCount > renders && page.Changes.Count > 0);
        Assert.AreEqual(1, page.Changes.Count);
        Assert.AreEqual(nextMode, page.Settings.AutoSave);
        Assert.AreEqual(nextMode == AutoSaveMode.Favorites, page.Settings.FavoritesOnly);
        Assert.AreSame(segmented, page.Segments.Single());
    }

    [UITestMethod]
    public async Task AliasAndIdentityRefreshWithoutReadingGlobalSettingsOrLeavingThePage()
    {
        await using var page = new PageFixture(AppSettings.Default with { Alias = "Owned initial alias" }, AppRuntimeState.Initial);
        await UntilAsync(() => page.Texts.Contains("Owned initial alias") && page.Texts.Contains("Loading identity…"));
        var segmented = page.Segments.Single();
        var invalidations = page.HeaderInvalidations;
        var fingerprint = string.Concat(Enumerable.Repeat("ABCD", 16));
        page.ReplaceSettings(page.Settings with { Alias = "Updated receiver 🌏", MinimizeToTray = true });
        page.ReplaceRuntime(page.Runtime with
        {
            NodeState = LocalSendNodeState.Running,
            Identity = new("Applied identity", "2.2", null, LocalSendDeviceType.Desktop, fingerprint, 53791, LocalSendProtocol.Https)
        });
        await UntilAsync(() => page.Texts.Contains("Updated receiver 🌏") && page.Texts.Contains("#981  #1")
            && !page.Texts.Contains("Loading identity…") && page.HeaderInvalidations > invalidations);
        Assert.IsFalse(page.Texts.Contains("Owned initial alias"));
        Assert.IsTrue(page.Texts.Any(text => text.Contains(fingerprint[..12], StringComparison.Ordinal)));
        Assert.IsFalse(page.Texts.Contains(fingerprint), "The UI shows the shortened fingerprint, not the full secret-independent identity string.");
        Assert.AreSame(segmented, page.Segments.Single());
        Assert.IsTrue(page.WebReceiveButton!.IsEnabled);
        Assert.IsEmpty(page.Changes);
    }

    [UITestMethod]
    public async Task PendingOffersUpdateTheBadgeAndRemoveTheIdlePlayerThenRecover()
    {
        await using var page = new PageFixture(AppSettings.Default with { Alias = "Owned offers receiver" }, AppRuntimeState.Initial);
        await UntilAsync(() => page.Players.Count == 1 && page.Segments.Count == 1);
        var segmented = page.Segments.Single();
        var peer = new LocalSendDevice("Synthetic peer", "2.2", null, LocalSendDeviceType.Desktop,
            new string('A', 64), false, [], DateTimeOffset.UtcNow);
        var first = new IncomingTransferRequest(Guid.NewGuid(), Guid.NewGuid(), "owned-first", peer, [], DateTimeOffset.UtcNow);
        var second = first with { RequestId = Guid.NewGuid(), TransferId = Guid.NewGuid(), SessionId = "owned-second" };
        page.ReplaceRuntime(page.Runtime with { IncomingTransfers = (IncomingTransferRequest[])[first, second] });
        await UntilAsync(() => page.Badges.Count == 1 && page.Badges[0].Value == 2 && page.Players.Count == 0);
        Assert.AreSame(segmented, page.Segments.Single());
        page.ReplaceRuntime(page.Runtime with { IncomingTransfers = (IncomingTransferRequest[])[second] });
        await UntilAsync(() => page.Badges.Count == 1 && page.Badges[0].Value == 1);
        page.ReplaceRuntime(page.Runtime with { IncomingTransfers = [] });
        await UntilAsync(() => page.Badges.Count == 0 && page.Players.Count == 1);
        Assert.AreSame(segmented, page.Segments.Single());
        Assert.IsEmpty(page.Changes);
    }

    private sealed class PageFixture : IAsyncDisposable
    {
        private Action<AppSettings>? _replaceSettings;
        private Action<AppRuntimeState>? _replaceRuntime;
        private Action? _invalidateHeader;
        public ReactorHostControl Host { get; } = new();
        public AppSettings Settings { get; private set; }
        public AppRuntimeState Runtime { get; private set; }
        public AppRoute Route { get; private set; }
        public List<AppSettings> Changes { get; } = [];
        public int RenderCount { get; private set; }
        public int HeaderInvalidations { get; private set; }
        public List<string> Texts => Descendants<TextBlock>(Host).Select(control => control.Text).ToList();
        public List<Segmented> Segments => Descendants<Segmented>(Host).ToList();
        public List<InfoBadge> Badges => Descendants<InfoBadge>(Host).ToList();
        public List<AnimatedVisualPlayer> Players => Descendants<AnimatedVisualPlayer>(Host).ToList();
        public Button? WebReceiveButton => Descendants<Button>(Host).SingleOrDefault(control => AutomationProperties.GetName(control) == "Receive via link");

        public PageFixture(AppSettings settings, AppRuntimeState runtime)
        {
            Settings = settings;
            Runtime = runtime;
            var header = new PageHeaderRightSlot
            {
                Invalidate = () => { HeaderInvalidations++; _invalidateHeader?.Invoke(); }
            };
            Host.OnRenderComplete = (_, _, _) => RenderCount++;
            TestApplication.TestWindow.Content = Host;
            Host.Mount(context =>
            {
                var (currentSettings, setSettings) = context.UseState(settings);
                var (currentRuntime, setRuntime) = context.UseState(runtime);
                var (_, bumpHeader) = context.UseReducer(0);
                _replaceSettings = setSettings;
                _replaceRuntime = setRuntime;
                _invalidateHeader = () => bumpHeader(value => value + 1);
                var navigation = context.UseNavigation(AppRoute.Receive);
                Route = navigation.CurrentRoute;
                return LocaleProvider("en-US", VStack(8,
                    Border(header.Owner == AppRoute.Receive ? header.Build?.Invoke() : null).WithKey("header"),
                    NavigationHost(navigation, route => route == AppRoute.Receive
                        ? Component<ReceivePage, ReceivePageProps>(new(currentRuntime, currentSettings, update =>
                        {
                            Settings = update(Settings);
                            Changes.Add(Settings);
                            setSettings(Settings);
                        }))
                        : TextBlock("Owned web destination")).WithKey("navigation"))
                    .Provide(PageHeader.RightSlot, header), global::Tonarink.AppShell.Resources, defaultLocale: "en-US");
            });
        }

        public void ReplaceSettings(AppSettings settings)
        {
            Assert.IsNotNull(_replaceSettings);
            Settings = settings;
            _replaceSettings(settings);
        }

        public void ReplaceRuntime(AppRuntimeState runtime)
        {
            Assert.IsNotNull(_replaceRuntime);
            Runtime = runtime;
            _replaceRuntime(runtime);
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
        var provider = new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(provider);
        provider.Invoke();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static async Task UntilAsync(Func<bool> condition, Func<string>? diagnostic = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) Assert.Fail("Timed out waiting for the real receive page. " + diagnostic?.Invoke());
            await Task.Delay(25);
        }
    }
}
