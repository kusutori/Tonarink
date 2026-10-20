using System.Globalization;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Components.Settings;
using Tonarink.Services.Localization;
using Tonarink.TestSupport;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsExpanderElement;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class FilePreviewContentTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    [TestCategory("SystemInput")]
    public async Task WinApp_RealFocusTypingHoverAndInvokeReachProductionPreviewControls()
    {
        using var directory = new TemporaryDirectory();
        var saves = new List<string>();
        var removed = false;
        var host = new ReactorHostControl();
        TestApplication.TestWindow.Content = host;
        var window = WinRT.Interop.WindowNative.GetWindowHandle(TestApplication.TestWindow)
            .ToInt64().ToString(CultureInfo.InvariantCulture);
        var executable = Environment.GetEnvironmentVariable("TONARINK_WINAPP_EXECUTABLE") ?? "winapp";
        try
        {
            var intl = AppIntl.For(AppSettings.Default with { LanguageIndex = 2 });
            host.Mount(_ => LocaleProvider("en-US",
                SettingsExpander(content: null, items:
                [FilePreviewProviderContent.CreateCard(intl,
                    new(FilePreviewProvider.QuickLook, "", saves.Add, () => removed = true))])
                    .Set(control => control.IsExpanded = true),
                global::Tonarink.AppShell.Resources, defaultLocale: "en-US"));
            await UntilAsync(() => Descendants<TextBox>(host).Any() && Descendants<Microsoft.UI.Xaml.Controls.Button>(host).Count(IsAction) == 2);
            var input = Descendants<TextBox>(host).Single();
            var actions = Descendants<Microsoft.UI.Xaml.Controls.Button>(host).Where(IsAction).ToArray();
            var verify = actions.Single(button => AutomationProperties.GetAutomationId(button).EndsWith(".Verify", StringComparison.Ordinal));
            var delete = actions.Single(button => AutomationProperties.GetAutomationId(button).EndsWith(".Delete", StringComparison.Ordinal));
            var entered = false;
            delete.PointerEntered += (_, _) => entered = true;

            await UiAsync("inspect", "--depth", "12");
            await UiAsync("get-property", "Settings.Preview.QuickLook.Verify", "--property", "IsEnabled");
            Assert.IsFalse(verify.IsEnabled);
            TestApplication.TestWindow.Activate();
            await UiAsync("click", "Settings.Preview.QuickLook.Path");
            await UiAsync("focus", "Settings.Preview.QuickLook.Path");
            Assert.AreNotEqual(FocusState.Unfocused, input.FocusState);
            await UiAsync("send-keys", "not-a-valid-preview-path", "--verbatim", "--via", "send-input", "--target", "Settings.Preview.QuickLook.Path");
            await UntilAsync(() => input.Text == "not-a-valid-preview-path" && verify.IsEnabled);
            Assert.IsEmpty(saves);
            await UiAsync("hover", "Settings.Preview.QuickLook.Delete");
            await UntilAsync(() => entered);
            Assert.IsFalse(removed, "Hover must not remove the item; this does not assert the animation's appearance.");
            await UiAsync("invoke", "Settings.Preview.QuickLook.Verify", "--action", "invoke");
            await UntilAsync(() => Descendants<FontIcon>(verify).Any(icon => icon.Glyph == "\uE783"));
            Assert.IsEmpty(saves);
            await UiAsync("invoke", "Settings.Preview.QuickLook.Delete", "--action", "invoke");
            await UntilAsync(() => removed);
        }
        finally
        {
            try { host.Dispose(); }
            finally { TestApplication.TestWindow.Content = new Grid(); }
            await Task.Yield();
        }

        async Task UiAsync(params string[] arguments)
        {
            var result = await ProcessRunner.RunAsync(executable,
                (string[])["ui", .. arguments, "--on", "local", "--window", window, "--json"],
                directory.Path, TimeSpan.FromSeconds(20));
            TestContext.WriteLine($"winapp ui {string.Join(' ', arguments)}: {result.StandardOutput} {result.StandardError}");
            Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
            using var json = JsonDocument.Parse(result.StandardOutput);
            Assert.AreEqual(JsonValueKind.Object, json.RootElement.ValueKind);
        }
    }

    [UITestMethod]
    public async Task Override_EmptyDisabled_DraftDoesNotSave_InvalidFeedback_ValidExplicitSave()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "custom preview.exe");
        await File.WriteAllTextAsync(path, "synthetic executable placeholder");
        var saves = new List<string>();
        var removed = false;
        var settings = AppSettings.Default with { LanguageIndex = 2 };
        var intl = AppIntl.For(settings);
        var renders = 0;
        var host = new ReactorHostControl { OnRenderComplete = (_, _, _) => renders++ };
        TestApplication.TestWindow.Content = host;
        try
        {
            host.Mount(_ => LocaleProvider("en-US",
                SettingsExpander(content: null, items:
                [FilePreviewProviderContent.CreateCard(intl,
                    new(FilePreviewProvider.QuickLook, "", saves.Add, () => removed = true))])
                    .Set(control => control.IsExpanded = true),
                global::Tonarink.AppShell.Resources, defaultLocale: "en-US"));
            await UntilAsync(() => Descendants<TextBox>(host).Any() && Descendants<Microsoft.UI.Xaml.Controls.Button>(host).Count(IsAction) == 2);
            var expander = Descendants<SettingsExpander>(host).Single();
            Assert.IsInstanceOfType<SettingsCard>(expander.Items[0]);
            var input = Descendants<TextBox>(host).Single();
            var actions = Descendants<Microsoft.UI.Xaml.Controls.Button>(host).Where(IsAction).ToArray();
            var verify = actions.Single(button => AutomationProperties.GetAutomationId(button).EndsWith(".Verify", StringComparison.Ordinal));
            var delete = actions.Single(button => AutomationProperties.GetAutomationId(button).EndsWith(".Delete", StringComparison.Ordinal));
            Assert.IsFalse(verify.IsEnabled);
            Assert.AreEqual("Settings.Preview.QuickLook.Path", AutomationProperties.GetAutomationId(input));
            Assert.AreEqual("Settings.Preview.QuickLook.Verify", AutomationProperties.GetAutomationId(verify));
            Assert.AreEqual("Settings.Preview.QuickLook.Delete", AutomationProperties.GetAutomationId(delete));

            input.Text = path + ".missing";
            await UntilAsync(() => verify.IsEnabled);
            Assert.IsEmpty(saves);
            input.Focus(FocusState.Programmatic);
            delete.Focus(FocusState.Programmatic);
            await Task.Yield();
            Assert.IsEmpty(saves, "Losing focus must not save the draft.");
            Invoke(verify);
            await UntilAsync(() => Descendants<FontIcon>(verify).Any(icon => icon.Glyph == "\uE783"));
            Assert.IsEmpty(saves, "Invalid paths must not be saved.");

            var previousRender = renders;
            input.Text = $" \"{path}\" ";
            await UntilAsync(() => renders > previousRender && verify.IsEnabled);
            Assert.AreEqual("Settings.Preview.QuickLook.Path", AutomationProperties.GetAutomationId(input));
            Assert.AreEqual("Settings.Preview.QuickLook.Verify", AutomationProperties.GetAutomationId(verify));
            Assert.AreEqual("Settings.Preview.QuickLook.Delete", AutomationProperties.GetAutomationId(delete));
            Invoke(verify);
            await UntilAsync(() => saves.Count == 1 && input.Text == path);
            Assert.AreEqual(path, saves[0]);
            Assert.AreEqual(path, input.Text);
            Invoke(delete);
            await UntilAsync(() => removed);
        }
        finally
        {
            try { host.Dispose(); }
            finally { TestApplication.TestWindow.Content = new Grid(); }
            await Task.Yield();
        }
    }

    private static bool IsAction(Microsoft.UI.Xaml.Controls.Button button)
    {
        var id = AutomationProperties.GetAutomationId(button);
        return id is "Settings.Preview.QuickLook.Verify" or "Settings.Preview.QuickLook.Delete";
    }

    private static void Invoke(Microsoft.UI.Xaml.Controls.Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var provider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(provider);
        provider.Invoke();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found)
            yield return found;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                Assert.Fail("Timed out waiting for preview controls.");
            await Task.Delay(25);
        }
    }
}
