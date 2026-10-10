using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Tonarink.Application;
using Tonarink.Blazor.Shared.Layout;
using Tonarink.Blazor.Shared.Pages;
using Tonarink.TestSupport;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

[assembly: DoNotParallelize]

namespace Tonarink.Blazor.Shared.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class PageBehaviorTests
{
    private BunitContext _context = null!;
    private TemporaryDirectory _directory = null!;
    private PlatformSpy _platform = null!;
    private TonarinkAppState _state = null!;
    private CultureInfo _culture = null!, _uiCulture = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        _directory = new();
        _platform = new(_directory.Path);
        _state = new(_platform);
        await _state.UpdateSettingsAsync(settings => settings with { Language = TonarinkLanguage.English });
        _platform.Saves.Clear();
        _context = new();
        // DOM-measuring/Fluent browser JS is a platform boundary; the production
        // Razor components and their event handlers are not substituted.
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddLogging();
        _context.Services.AddLocalization();
        _context.Services.AddFluentUIComponents();
        _context.Services.AddSingleton(_state);
        _context.Services.AddSingleton<IPlatformServices>(_platform);
        _context.Services.AddSingleton<ITonarinkRuntime>(new CapabilityOnlyRuntime(_platform.Capabilities));
        _context.Renderer.SetRendererInfo(new RendererInfo("WebView", isInteractive: true));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        _directory?.Dispose();
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    [TestMethod]
    public void NavigationOffersPrimaryRoutesAndRespondsToRequestedRoute()
    {
        var cut = _context.Render<MainLayout>();
        // FluentLayout renders desktop navigation and its mobile drawer.
        foreach (var navigation in cut.FindComponents<FluentNav>())
            Assert.AreSequenceEqual(new[] { "/", "/send", "/settings" },
                navigation.FindComponents<FluentNavItem>().Select(item => item.Instance.Href));
        Assert.IsNotEmpty(cut.FindComponents<FluentNav>());
        _state.RequestNavigation("/send");
        cut.WaitForAssertion(() => Assert.AreEqual("http://localhost/send",
            _context.Services.GetRequiredService<NavigationManager>().Uri));
        Assert.IsNull(_state.TakeRequestedRoute());
    }

    [TestMethod]
    public void ReceiveAddsIncomingLinkAndKeepsHistorySecondary()
    {
        var cut = _context.Render<Receive>();
        Assert.IsEmpty(cut.FindAll("a.row-item"));
        var item = new ShareItem(Guid.NewGuid(), "hello.txt", 5, "text/plain");
        _state.ReplaceIncomingOffers([new("request-1", "Sender", "Desktop", [item], DateTimeOffset.UtcNow)]);
        cut.WaitForAssertion(() =>
        {
            var link = cut.Find("a.row-item");
            Assert.AreEqual("/receive/request-1", link.GetAttribute("href"));
            Assert.Contains("Sender", link.TextContent, StringComparison.Ordinal);
        });
        Assert.IsNotNull(cut.Find("[href='/history']"));
        _state.ReplaceIncomingOffers([]);
        cut.WaitForAssertion(() => Assert.IsEmpty(cut.FindAll("a.row-item")));
    }

    [TestMethod]
    public async Task SettingsSwitchSavesAndExternalStateChangeUpdatesMountedControl()
    {
        var cut = _context.Render<Settings>();
        var toggle = cut.FindComponent<FluentSwitch>();
        Assert.IsFalse(toggle.Instance.Value);
        await cut.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(true));
        Assert.IsTrue(_state.Settings.AutoAccept);
        Assert.IsTrue(Assert.ContainsSingle(_platform.Saves).AutoAccept);
        await _state.UpdateSettingsAsync(settings => settings with { AutoAccept = false });
        cut.WaitForAssertion(() => Assert.IsFalse(toggle.Instance.Value));
    }

    [TestMethod]
    public void SendNativePickerCapabilitiesControlActions()
    {
        var cut = _context.Render<Send>();
        var actions = cut.FindComponents<FluentButton>()
            .Where(button => button.Instance.Class == "action-tile").ToArray();
        Assert.HasCount(4, actions);
        Assert.IsFalse(actions[0].Instance.Disabled);
        Assert.IsTrue(actions[1].Instance.Disabled);
        Assert.IsFalse(actions[2].Instance.Disabled);
        Assert.IsFalse(actions[3].Instance.Disabled);
        Assert.IsTrue(cut.FindComponents<FluentButton>()
            .Single(button => button.Instance.IconStart is Icons.Regular.Size20.Desktop).Instance.Disabled);
    }

    [TestMethod]
    public async Task SettingsRendersInputsAndPersistsThemeAndLanguageSelections()
    {
        var cut = _context.Render<Settings>();
        var inputs = cut.FindComponents<FluentTextInput>();
        Assert.HasCount(3, inputs);
        Assert.AreEqual(_state.Settings.Alias, inputs[0].Instance.Value);
        Assert.AreEqual(_platform.DownloadDirectory, inputs[1].Instance.Value);
        Assert.IsTrue(inputs[1].Instance.Disabled);
        Assert.AreEqual(TextInputType.Password, inputs[2].Instance.TextInputType);
        var selections = cut.FindComponents<FluentSelect<string, string>>();
        Assert.HasCount(2, selections);
        await cut.InvokeAsync(() => selections[0].Instance.ValueChanged.InvokeAsync(nameof(TonarinkTheme.Dark)));
        Assert.AreEqual(TonarinkTheme.Dark, _state.Settings.Theme);
        await cut.InvokeAsync(() => selections[1].Instance.ValueChanged.InvokeAsync(nameof(TonarinkLanguage.SimplifiedChinese)));
        Assert.AreEqual(TonarinkLanguage.SimplifiedChinese, _state.Settings.Language);
        var resources = new System.Resources.ResourceManager(typeof(Tonarink.Blazor.Shared.Resources.SharedResources));
        Assert.AreEqual("设置", resources.GetString("Settings", CultureInfo.GetCultureInfo("zh-CN")),
            "The shipped Chinese satellite must resolve independently of the renderer's execution context.");
        cut.WaitForAssertion(() => Assert.AreEqual("设置", cut.Find("h1").TextContent,
            $"Selected language: {_state.Settings.Language}; observing culture: {CultureInfo.CurrentUICulture.Name}"));
        Assert.AreEqual(2, _platform.Saves.Count);
        await Task.Run(() => _state.UpdateSettingsAsync(settings => settings with { AutoAccept = true }));
        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(cut.FindComponent<FluentSwitch>().Instance.Value);
            Assert.AreEqual("设置", cut.Find("h1").TextContent,
                "An unrelated background state change must not revert the selected language.");
        });
        await Task.Run(() => _state.UpdateSettingsAsync(settings => settings with { Language = TonarinkLanguage.English }));
        cut.WaitForAssertion(() => Assert.AreEqual("Settings", cut.Find("h1").TextContent));
        Assert.AreEqual(4, _platform.Saves.Count);
    }

    [TestMethod]
    public async Task SendTextDialogAddsTrimmedUnicodeContentAndCloses()
    {
        var cut = _context.Render<Send>();
        var open = cut.FindComponents<FluentButton>()
            .Single(button => button.Instance.IconStart is Icons.Regular.Size24.TextDescription);
        await cut.InvokeAsync(() => open.Instance.OnClick.InvokeAsync());
        Assert.HasCount(1, cut.FindAll("section[role=dialog]"));
        var input = cut.FindComponent<FluentTextArea>();
        await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("  你好 🌏  "));
        var add = cut.FindComponents<FluentButton>()
            .Single(button => button.Instance.Appearance == ButtonAppearance.Primary);
        Assert.IsFalse(add.Instance.Disabled);
        await cut.InvokeAsync(() => add.Instance.OnClick.InvokeAsync());
        var queued = Assert.ContainsSingle(_state.SendItems);
        Assert.AreEqual("你好 🌏", queued.TextContent);
        Assert.AreEqual(System.Text.Encoding.UTF8.GetByteCount(queued.TextContent!), queued.Size);
        Assert.IsEmpty(cut.FindAll("section[role=dialog]"));
    }

    [TestMethod]
    public void HistoryUpdatesSameTransferRatherThanAddingDuplicateRow()
    {
        var cut = _context.Render<History>();
        var activity = new TransferActivity("transfer-1", TransferDirection.Receive, "Peer", "hello.txt",
            TransferStatus.Transferring, 25, 100, DateTimeOffset.UtcNow);
        _state.UpsertTransfer(activity);
        cut.WaitForAssertion(() => Assert.AreEqual(25, Assert.ContainsSingle(cut.FindComponents<FluentProgressBar>()).Instance.Value));
        _state.UpsertTransfer(activity with { Status = TransferStatus.Completed, BytesTransferred = 100 });
        cut.WaitForAssertion(() =>
        {
            Assert.HasCount(1, cut.FindAll(".transfer-row"));
            Assert.AreEqual(100, Assert.ContainsSingle(cut.FindComponents<FluentProgressBar>()).Instance.Value);
        });
    }

    [TestMethod]
    [TestCategory("Distribution")]
    public void HybridHostReferencesShippedFluentStyles()
    {
        // This is explicitly a host-asset contract, not UI automation.
        var host = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ShippedHost", "index.html"));
        Assert.Contains("Microsoft.FluentUI.AspNetCore.Components/css/reboot.css", host, StringComparison.Ordinal);
        Assert.Contains("Tonarink.Blazor.Shared/theme.css", host, StringComparison.Ordinal);
        Assert.Contains("Tonarink.Blazor.Shared/tonarink.css", host, StringComparison.Ordinal);
    }

    private sealed class PlatformSpy(string root) : IPlatformServices
    {
        public List<TonarinkSettings> Saves { get; } = [];
        public PlatformCapabilities Capabilities => new("Test", true, false, false, true, false, true, false, []);
        public string DataDirectory => Path.Combine(root, "data");
        public string DownloadDirectory => Path.Combine(root, "downloads");
        public string DefaultAlias => "Component test";
        public string DeviceModel => "Test";
        public TonarinkDeviceKind DeviceKind => TonarinkDeviceKind.Desktop;
        public Task<TonarinkSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult<TonarinkSettings?>(null);
        public Task SaveSettingsAsync(TonarinkSettings settings, CancellationToken cancellationToken = default)
        {
            Saves.Add(settings);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ShareItem>> PickFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShareItem>>([]);
        public Task<IReadOnlyList<ShareItem>> PickFoldersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShareItem> ImportSharedFileAsync(string fileName, Stream source, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ReleaseShareItem(ShareItem item) { }
        public void ReleaseUnreferencedShareItems(IReadOnlyList<ShareItem> stillHeld) { }
        public ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
