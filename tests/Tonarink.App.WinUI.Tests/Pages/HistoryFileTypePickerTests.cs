using System.Collections.Immutable;
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
using Tonarink.Pages.History;
using Tonarink.Utilities;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class HistoryFileTypePickerTests
{
    [UITestMethod]
    public async Task NativeContentTogglesKeepIconsLabelsAndIndependentSelectionAcrossRenders()
    {
        var host = new ReactorHostControl { Width = 640, Height = 480 };
        var selection = ImmutableHashSet<FileTypeGlyphKind>.Empty;
        var changes = 0;
        var renders = 0;
        host.OnRenderComplete = (_, _, _) => renders++;
        TestApplication.TestWindow.Content = host;
        try
        {
            host.Mount(context =>
            {
                var (selected, setSelected) = context.UseState(selection);
                return LocaleProvider("en-US",
                    Component<HistoryFileTypePicker, HistoryFileTypePickerProps>(new(selected, value =>
                    {
                        selection = value;
                        changes++;
                        setSelected(value);
                    })), global::Tonarink.AppShell.Resources, defaultLocale: "en-US");
            });

            await UntilAsync(() => FindType(host, FileTypeGlyphKind.Image) is not null);
            var image = FindType(host, FileTypeGlyphKind.Image)!;
            var text = FindType(host, FileTypeGlyphKind.Text)!;
            await UntilAsync(() => Descendants<FontIcon>(image).Any());
            Assert.IsInstanceOfType<FrameworkElement>(image.Content);
            Assert.IsTrue(Descendants<TextBlock>(image).Any(block => block.Text == "Images"));
            Assert.AreEqual(FileTypeGlyphs.ForKind(FileTypeGlyphKind.Image), Descendants<FontIcon>(image).Single().Glyph);
            Assert.AreEqual(0, changes, "Mounting controlled values must not report user edits.");

            await ToggleAndRenderAsync(image);
            Assert.Contains(FileTypeGlyphKind.Image, selection);
            Assert.IsTrue(image.IsChecked);
            await ToggleAndRenderAsync(text);
            Assert.AreEqual(2, selection.Count);
            Assert.IsTrue(text.IsChecked);
            Assert.AreSame(image, FindType(host, FileTypeGlyphKind.Image));
            Assert.AreSame(text, FindType(host, FileTypeGlyphKind.Text));
            Assert.IsTrue(image.IsChecked);
            Assert.AreEqual(2, changes, "Each native toggle should dispatch exactly one selection change.");

            await ToggleAndRenderAsync(image);
            Assert.AreEqual(1, selection.Count);
            Assert.IsFalse(image.IsChecked);
            Assert.Contains(FileTypeGlyphKind.Text, selection);
            Assert.IsTrue(text.IsChecked);
            Assert.AreEqual(3, changes);
            Assert.IsTrue(Descendants<FontIcon>(image).Any(), "Content must survive checked-state reconciliation.");
        }
        finally
        {
            host.Dispose();
            TestApplication.TestWindow.Content = new Grid();
        }

        async Task ToggleAndRenderAsync(ToggleButton button)
        {
            var previousRender = renders;
            Toggle(button);
            await UntilAsync(() => renders > previousRender);
        }
    }

    private static ToggleButton? FindType(DependencyObject root, FileTypeGlyphKind kind) =>
        Descendants<ToggleButton>(root).SingleOrDefault(button =>
            AutomationProperties.GetAutomationId(button) == $"History.Filter.Type.{kind}");

    private static void Toggle(ToggleButton button)
    {
        var provider = new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle) as IToggleProvider;
        Assert.IsNotNull(provider);
        provider.Toggle();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                Assert.Fail("Timed out waiting for the history type picker to update.");
            await Task.Delay(25);
        }
    }
}
