using System.Net;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Components.Devices;
using Tonarink.Hooks.LocalSendNode;
using Tonarink.Pages.Send;
using Tonarink.Services.Devices;
using Tonarink.Utilities;
using static Microsoft.UI.Reactor.Factories;
using XamlMenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class SendDevicesCardTests
{
    [UITestMethod]
    public async Task DeviceIdentityCardPublishesItsMountedAnchorAndClearsItOnUnmount()
    {
        var changes = new List<FrameworkElement?>();
        var host = new ReactorHostControl();
        TestApplication.TestWindow.Content = host;
        try
        {
            host.Mount(context =>
            {
                return LocaleProvider("en-US", Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                    "Owned card", null, LocalSendDeviceType.Desktop, "#1",
                    ElementChanged: changes.Add)), global::Tonarink.AppShell.Resources, defaultLocale: "en-US");
            });
            await UntilAsync(() => changes.Count == 1);
            Assert.IsNotNull(changes[0]);
            Assert.AreSame(host.XamlRoot, changes[0]!.XamlRoot);
            host.Dispose();
            Assert.AreEqual(2, changes.Count);
            Assert.IsNull(changes[1]);
        }
        finally
        {
            try { host.Dispose(); }
            finally { TestApplication.TestWindow.Content = new Grid(); }
            await Task.Yield();
        }
    }

    [UITestMethod]
    public async Task LifecycleSendingAndResolvingUpdateNativeGatesWithoutRecreatingActions()
    {
        await using var card = new CardFixture();
        await card.ReadyAsync();
        var actions = card.Buttons.ToArray();
        var multiple = card.Multiple!;
        foreach (var state in Enum.GetValues<LocalSendNodeState>())
        {
            var renders = card.RenderCount;
            card.Replace(card.Props with { Runtime = card.Props.Runtime with { NodeState = state } });
            await UntilAsync(() => card.RenderCount > renders);
            AssertGates(card, canSend: state == LocalSendNodeState.Running,
                canResolve: state == LocalSendNodeState.Running, canShare: state == LocalSendNodeState.Running,
                canRefresh: true, canKeep: true);
            Assert.AreSequenceEqual(actions, card.Buttons, "State updates must not replace native actions or peer buttons.");
            Assert.AreSame(multiple, card.Multiple);
        }

        var resolvingRender = card.RenderCount;
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { NodeState = LocalSendNodeState.Running }, IsResolvingAddress = true });
        await UntilAsync(() => card.RenderCount > resolvingRender && !card.Button("Send to address").IsEnabled);
        AssertGates(card, canSend: true, canResolve: false, canShare: true, canRefresh: true, canKeep: true);
        card.Replace(card.Props with { IsResolvingAddress = false, IsSending = true });
        await UntilAsync(() => !card.Button("Refresh nearby devices").IsEnabled);
        AssertGates(card, canSend: false, canResolve: false, canShare: false, canRefresh: false, canKeep: false);
        card.Replace(card.Props with { IsSending = false });
        await UntilAsync(() => card.Button("Refresh nearby devices").IsEnabled);
        AssertGates(card, canSend: true, canResolve: true, canShare: true, canRefresh: true, canKeep: true);
        Assert.AreSequenceEqual(actions, card.Buttons);
        Assert.IsEmpty(card.Calls, "Reconciliation alone must not dispatch user actions.");

        foreach (var name in new[] { "Refresh nearby devices", "Send to address", "Favorites", "Share via link" })
            Invoke(card.Button(name));
        Toggle(multiple);
        await UntilAsync(() => card.Calls.Count == 5 && multiple.IsChecked == true);
        Assert.AreSequenceEqual(new[] { "refresh", "address", "favorites", "web", "keep:True" }, card.Calls);
        Assert.IsTrue(card.Props.KeepItemsForMultipleReceivers);
        Toggle(multiple);
        await UntilAsync(() => card.Calls.Count == 6 && multiple.IsChecked == false);
        Assert.AreEqual("keep:False", card.Calls[^1]);
    }

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FingerprintKeysPreservePeerControlsAndCallbacksUseLatestSnapshot(bool wide)
    {
        await using var card = new CardFixture(wide);
        await card.ReadyAsync();
        var first = card.Props.Runtime.Devices[0];
        var second = card.Props.Runtime.Devices[1];
        var firstButton = card.Button("Send to Alpha");
        var secondButton = card.Button("Send to Beta");
        Invoke(firstButton);
        await UntilAsync(() => card.Selected.Count == 1);
        Assert.AreSame(first, card.Selected[0].Device);
        Assert.IsNotNull(card.Selected[0].Source);
        Assert.AreSame(card.Host.XamlRoot, card.Selected[0].Source!.XamlRoot);

        var current = first with
        {
            Alias = "Updated Alpha",
            DeviceModel = "Updated model",
            Endpoints = (DeviceEndpoint[])[new(IPAddress.Parse("192.0.2.48"), 1234, LocalSendProtocol.Https)]
        };
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { Devices = (LocalSendDevice[])[second, current] } });
        await UntilAsync(() => card.Buttons.Any(button => Name(button) == "Send to Updated Alpha"));
        Assert.AreSame(firstButton, card.Button("Send to Updated Alpha"));
        Assert.AreSame(secondButton, card.Button("Send to Beta"));
        Assert.AreSequenceEqual(new[] { secondButton, firstButton }, card.Buttons.Where(IsPeer));
        Assert.Contains("Updated model", card.Texts);
        Assert.Contains("#48", card.Texts);
        Invoke(firstButton);
        Invoke(card.Button("View details for Updated Alpha"));
        await UntilAsync(() => card.Selected.Count == 2 && card.Details.Count == 1);
        Assert.AreSame(current, card.Selected[1].Device, "A reused native button must not retain the old device snapshot.");
        Assert.AreSame(card.Selected[0].Source, card.Selected[1].Source);
        Assert.AreSame(current, card.Details[0]);

        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        secondButton.Unloaded += (_, _) => unloaded.TrySetResult();
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { Devices = (LocalSendDevice[])[current] } });
        await UntilAsync(() => card.Buttons.Count(IsPeer) == 1);
        await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreSame(firstButton, card.Button("Send to Updated Alpha"));
        Assert.IsFalse(card.Buttons.Contains(secondButton));
        var replacement = Device("Gamma", 'C', 3);
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { Devices = (LocalSendDevice[])[replacement, current] } });
        await UntilAsync(() => card.Buttons.Count(IsPeer) == 2);
        Assert.AreSame(firstButton, card.Button("Send to Updated Alpha"));
        Invoke(card.Button("Send to Gamma"));
        await UntilAsync(() => card.Selected.Count == 3);
        Assert.AreSame(replacement, card.Selected[2].Device);
    }

    [UITestMethod]
    public async Task FavoriteNameIndicatorAndNativeContextActionsFollowCurrentProps()
    {
        await using var card = new CardFixture();
        await card.ReadyAsync();
        var device = card.Props.Runtime.Devices[0];
        var peer = card.Button("Send to Alpha");
        await InvokeMenuAsync(peer, "Favorite");
        await UntilAsync(() => card.Favorites.Count == 1);
        Assert.AreSame(device, card.Favorites[0].Device);
        Assert.IsNull(card.Favorites[0].Favorite);
        await InvokeMenuAsync(peer, "Verify");
        await UntilAsync(() => card.Verified.Count == 1);
        Assert.AreSame(device, card.Verified[0]);

        var favorite = new FavoriteDevice(device.Fingerprint, "Saved Alpha", "192.0.2.1", 53317);
        card.Replace(card.Props with { Favorites = new Dictionary<string, FavoriteDevice> { [device.Fingerprint] = favorite } });
        await UntilAsync(() => card.Buttons.Any(button => Name(button) == "Send to Saved Alpha"));
        Assert.AreSame(peer, card.Button("Send to Saved Alpha"));
        Assert.Contains("Saved Alpha", card.Texts);
        Assert.AreEqual(1, Descendants<TextBlock>(card.Host).Count(text => text.Text == AppIcons.FavoriteIndicator));
        await InvokeMenuAsync(peer, "Unfavorite");
        await UntilAsync(() => card.Favorites.Count == 2);
        Assert.AreSame(device, card.Favorites[1].Device);
        Assert.AreSame(favorite, card.Favorites[1].Favorite);

        card.Replace(card.Props with { Favorites = new Dictionary<string, FavoriteDevice>() });
        await UntilAsync(() => card.Buttons.Any(button => Name(button) == "Send to Alpha"));
        Assert.AreSame(peer, card.Button("Send to Alpha"));
        Assert.IsFalse(Descendants<TextBlock>(card.Host).Any(text => text.Text == AppIcons.FavoriteIndicator));
        await InvokeMenuAsync(peer, "Favorite");
        await UntilAsync(() => card.Favorites.Count == 3);
        Assert.IsNull(card.Favorites[2].Favorite, "Menu actions must not retain a removed favorite snapshot.");
    }

    [UITestMethod]
    public async Task EmptyWarningFaultAndRecoveryReplaceOnlyTheDeviceBody()
    {
        await using var card = new CardFixture();
        await card.ReadyAsync();
        var address = card.Button("Send to address");
        var devices = card.Props.Runtime.Devices;
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { Devices = [] } });
        await UntilAsync(() => card.Texts.Contains("Looking for nearby devices"));
        Assert.Contains("Make sure both devices are connected to the same local network", card.Texts);
        Assert.IsFalse(card.Buttons.Any(IsPeer));
        Assert.IsTrue(address.IsEnabled);
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { DiscoveryWarning = "synthetic multicast failure" } });
        await UntilAsync(() => card.Texts.Contains("Multicast discovery is unavailable; scanning the local network for nearby devices"));
        Assert.IsFalse(card.Texts.Contains("Make sure both devices are connected to the same local network"));
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { NodeState = LocalSendNodeState.Faulted } });
        await UntilAsync(() => card.Texts.Contains("Could not start the network service"));
        Assert.Contains("Check whether port 53317 is in use by Tonarink or another LocalSend-compatible app", card.Texts);
        Assert.IsFalse(address.IsEnabled);
        card.Replace(card.Props with { Runtime = card.Props.Runtime with { NodeState = LocalSendNodeState.Running, Devices = devices, DiscoveryWarning = null } });
        await UntilAsync(() => card.Buttons.Count(IsPeer) == 2 && address.IsEnabled);
        Assert.AreSame(address, card.Button("Send to address"));
        Assert.IsFalse(card.Texts.Contains("Could not start the network service"));
        Assert.IsEmpty(card.Calls);
    }

    private static void AssertGates(CardFixture card, bool canSend, bool canResolve, bool canShare, bool canRefresh, bool canKeep)
    {
        foreach (var peer in card.Buttons.Where(IsPeer)) Assert.AreEqual(canSend, peer.IsEnabled, Name(peer));
        Assert.AreEqual(canResolve, card.Button("Send to address").IsEnabled);
        Assert.AreEqual(canResolve, card.Button("Favorites").IsEnabled);
        Assert.AreEqual(canShare, card.Button("Share via link").IsEnabled);
        Assert.AreEqual(canRefresh, card.Button("Refresh nearby devices").IsEnabled);
        Assert.AreEqual(canKeep, card.Multiple!.IsEnabled);
    }

    private sealed class CardFixture : IAsyncDisposable
    {
        private Action<SendDevicesCardProps>? _replace;
        public ReactorHostControl Host { get; }
        public SendDevicesCardProps Props { get; private set; }
        public int RenderCount { get; private set; }
        public List<string> Calls { get; } = [];
        public List<(LocalSendDevice Device, FrameworkElement? Source)> Selected { get; } = [];
        public List<LocalSendDevice> Details { get; } = [];
        public List<(LocalSendDevice Device, FavoriteDevice? Favorite)> Favorites { get; } = [];
        public List<LocalSendDevice> Verified { get; } = [];
        public List<Button> Buttons => Descendants<Button>(Host).ToList();
        public ToggleButton? Multiple => Descendants<ToggleButton>(Host).SingleOrDefault();
        public List<string> Texts => Descendants<TextBlock>(Host).Select(text => text.Text).ToList();
        public Button Button(string name) => Buttons.Single(button => Name(button) == name);

        public async Task ReadyAsync()
        {
            try
            {
                await UntilAsync(() => Multiple is not null && Buttons.Count(IsPeer) == 2
                    && new[] { "Refresh nearby devices", "Send to address", "Favorites", "Share via link",
                        "View details for Alpha", "View details for Beta" }
                    .All(name => Buttons.Any(button => Name(button) == name)));
            }
            catch (AssertFailedException exception)
            {
                throw new AssertFailedException($"{exception.Message} Renders: {RenderCount}; buttons: {string.Join(" | ", Buttons.Select(Name))}; text: {string.Join(" | ", Texts)}", exception);
            }
        }

        public CardFixture(bool wide = false)
        {
            Props = new(
                AppRuntimeState.Initial with { NodeState = LocalSendNodeState.Running, Devices = (LocalSendDevice[])[Device("Alpha", 'A', 1), Device("Beta", 'B', 2)] },
                new Dictionary<string, FavoriteDevice>(), wide,
                IsSending: false, IsResolvingAddress: false, IsAddressDialogOpen: false, IsFavoritesDialogOpen: false,
                KeepItemsForMultipleReceivers: false,
                SetKeepItemsForMultipleReceivers: value =>
                {
                    Calls.Add($"keep:{value}");
                    Replace(Props! with { KeepItemsForMultipleReceivers = value });
                },
                SearchingAnimation: Empty(),
                RefreshAsync: () => { Calls.Add("refresh"); return Task.CompletedTask; },
                OpenAddress: () => Calls.Add("address"),
                OpenFavorites: () => Calls.Add("favorites"),
                OpenWebShare: () => Calls.Add("web"),
                SelectDevice: (device, source) => Selected.Add((device, source)),
                OpenDetails: Details.Add,
                ChangeFavorite: (device, favorite) => Favorites.Add((device, favorite)),
                VerifyDevice: Verified.Add);
            Host = new ReactorHostControl { OnRenderComplete = (_, _, _) => RenderCount++ };
            TestApplication.TestWindow.Content = Host;
            Host.Mount(context =>
            {
                var (props, replace) = context.UseState(Props);
                _replace = replace;
                return LocaleProvider("en-US", Component<SendDevicesCard, SendDevicesCardProps>(props),
                    global::Tonarink.AppShell.Resources, defaultLocale: "en-US");
            });
        }

        public void Replace(SendDevicesCardProps props)
        {
            Assert.IsNotNull(_replace);
            Props = props;
            _replace(props);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var element in Descendants<FrameworkElement>(Host))
                    if (element.ContextFlyout is XamlMenuFlyout flyout) flyout.Hide();
            }
            finally
            {
                // Unloaded may be a temporary reparent, so Reactor deliberately
                // keeps its tree alive. This fixture owns the terminal teardown.
                try { Host.Dispose(); }
                finally { TestApplication.TestWindow.Content = new Grid(); }
            }
            await Task.Yield();
        }
    }

    private static LocalSendDevice Device(string alias, char fingerprint, byte lastOctet) => new(
        alias, "2.1", "Synthetic model", LocalSendDeviceType.Desktop, new string(fingerprint, 64), false,
        (DeviceEndpoint[])[new(IPAddress.Parse($"192.0.2.{lastOctet}"), 53317, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);

    private static string Name(DependencyObject element) => AutomationProperties.GetName(element);
    private static bool IsPeer(Button button) => Name(button).StartsWith("Send to ", StringComparison.Ordinal) && Name(button) != "Send to address";

    private static void Invoke(Button button)
    {
        var provider = new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(provider);
        provider.Invoke();
    }

    private static void Toggle(ToggleButton button)
    {
        var provider = new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle) as IToggleProvider;
        Assert.IsNotNull(provider);
        provider.Toggle();
    }

    private static async Task InvokeMenuAsync(Button peer, string name)
    {
        FrameworkElement? owner = peer;
        while (owner is not null && owner.ContextFlyout is not XamlMenuFlyout)
            owner = VisualTreeHelper.GetParent(owner) as FrameworkElement;
        Assert.IsNotNull(owner);
        var flyout = (XamlMenuFlyout)owner.ContextFlyout;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object> onOpened = (_, _) => opened.TrySetResult();
        EventHandler<object> onClosed = (_, _) => closed.TrySetResult();
        flyout.Opened += onOpened;
        flyout.Closed += onClosed;
        try
        {
            flyout.ShowAt(peer);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var item = flyout.Items.OfType<MenuFlyoutItem>().Single(candidate => candidate.Text == name);
            var provider = new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Assert.IsNotNull(provider);
            provider.Invoke();
        }
        finally
        {
            flyout.Hide();
            try
            {
                if (opened.Task.IsCompletedSuccessfully) await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                flyout.Opened -= onOpened;
                flyout.Closed -= onClosed;
            }
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
            if (DateTime.UtcNow >= deadline) Assert.Fail("Timed out waiting for the production send devices card.");
            await Task.Delay(25);
        }
    }
}
