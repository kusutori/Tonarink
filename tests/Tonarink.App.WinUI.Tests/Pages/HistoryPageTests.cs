using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Pages.History;
using Tonarink.Services.History;
using Tonarink.TestSupport;
using static Microsoft.UI.Reactor.Factories;
using XamlMenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("UI")]
public sealed class HistoryPageTests
{
    [UITestMethod]
    public async Task StoreUpdatesEmptyRowsAdditionRemovalAndClearingWithoutLeakingSubscriptions()
    {
        await using var directory = new TemporaryDirectory();
        var store = new MemoryHistoryStore();
        var page = new PageFixture(directory.Path, store);
        try
        {
            await UntilAsync(() => page.Texts.Contains("No receive history yet") && store.SubscriberCount == 1);
            Assert.IsFalse(page.ClearButton.IsEnabled);
            var first = Entry(directory.Path, "first.txt");
            var second = Entry(directory.Path, "second.txt");
            store.Replace(first, second);
            await UntilAsync(() => page.RowButtons.Count == 2);
            Assert.IsTrue(page.ClearButton.IsEnabled);
            Assert.IsFalse(page.Texts.Contains("No receive history yet"));
            var secondButton = page.RowButton(second);
            store.Replace(second);
            await UntilAsync(() => page.RowButtons.Count == 1);
            Assert.AreSame(secondButton, page.RowButton(second));
            Assert.IsFalse(page.Texts.Contains(first.FileName));
            store.Replace();
            await UntilAsync(() => page.Texts.Contains("No receive history yet") && !page.ClearButton.IsEnabled);
            Assert.AreEqual(1, store.SubscriberCount);
            Assert.IsEmpty(store.Removed);
            Assert.AreEqual(0, store.ClearCount, "External notifications aren't user clear commands.");
        }
        finally { await page.DisposeAsync(); }
        Assert.AreEqual(0, store.SubscriberCount);
    }

    [UITestMethod]
    [DataRow("file")]
    [DataRow("directory")]
    [DataRow("missing")]
    public async Task NativeMenuGatesMissingPathsAndDeletesOnlyTheSelectedRecord(string pathKind)
    {
        await using var directory = new TemporaryDirectory();
        var target = Entry(directory.Path, "target.txt");
        var retained = Entry(directory.Path, "retained.txt");
        if (pathKind == "file") File.WriteAllText(target.Path, "Owned file must remain");
        if (pathKind == "directory") Directory.CreateDirectory(target.Path);
        File.WriteAllText(retained.Path, "Retained bytes");
        var store = new MemoryHistoryStore(target, retained);
        await using var page = new PageFixture(directory.Path, store);
        await UntilAsync(() => page.RowButtons.Count == 2);
        await WithMenuAsync(page.RowButton(target), items =>
        {
            Assert.AreEqual(pathKind != "missing", items.Single(item => item.Text == "Open file").IsEnabled);
            Assert.AreEqual(pathKind != "missing", items.Single(item => item.Text == "Show in folder").IsEnabled);
            Assert.IsTrue(items.Single(item => item.Text == "Info").IsEnabled);
            var delete = items.Single(item => item.Text == "Remove from history");
            Assert.IsTrue(delete.IsEnabled);
            Invoke(delete);
        });
        await UntilAsync(() => page.RowButtons.Count == 1);
        Assert.AreSequenceEqual(new[] { target.Id }, store.Removed);
        Assert.AreSequenceEqual(new[] { retained.Id }, store.Entries.Select(entry => entry.Id));
        Assert.AreEqual("Retained bytes", File.ReadAllText(retained.Path));
        Assert.AreEqual(pathKind == "file", File.Exists(target.Path));
        Assert.AreEqual(pathKind == "directory", Directory.Exists(target.Path));
    }

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClearRequiresConfirmationAndNeverDeletesReceivedFiles(bool confirm)
    {
        await using var directory = new TemporaryDirectory();
        var entry = Entry(directory.Path, "owned.txt");
        File.WriteAllText(entry.Path, "Owned received bytes");
        var store = new MemoryHistoryStore(entry);
        await using var page = new PageFixture(directory.Path, store);
        await UntilAsync(() => page.RowButtons.Count == 1);
        Invoke(page.ClearButton);
        await UntilAsync(() => page.OpenDialog is not null);
        var dialog = page.OpenDialog!;
        Assert.AreEqual("Delete receive history?", dialog.Title);
        Assert.AreEqual(0, store.ClearCount);
        await ClickDialogButtonAsync(dialog, confirm ? "PrimaryButton" : "SecondaryButton");
        await UntilAsync(() => page.OpenDialog is null && store.ClearCount == (confirm ? 1 : 0));
        Assert.AreEqual(confirm ? 0 : 1, store.Entries.Count);
        Assert.AreEqual("Owned received bytes", File.ReadAllText(entry.Path));
        if (confirm)
            await UntilAsync(() => page.Texts.Contains("No receive history yet") && !page.ClearButton.IsEnabled);
        else
            Assert.IsTrue(page.ClearButton.IsEnabled);
    }

    [UITestMethod]
    public async Task NavigationShowsActualEntryInfoAndConsumesTheRequestOnClose()
    {
        await using var directory = new TemporaryDirectory();
        var entry = Entry(directory.Path, "报告 🌏.txt");
        var store = new MemoryHistoryStore(entry);
        await using var page = new PageFixture(directory.Path, store, entry.Id);
        await UntilAsync(() => page.OpenDialog is not null);
        var dialog = page.OpenDialog!;
        Assert.AreEqual("File info", dialog.Title);
        var texts = Descendants<TextBlock>(dialog).Select(block => block.Text).ToList();
        Assert.Contains(entry.FileName, texts);
        Assert.Contains(entry.Path, texts);
        Assert.Contains(entry.SenderAlias, texts);
        Assert.Contains("123 B", texts);
        Assert.Contains(entry.ReceivedAt.ToLocalTime().ToString("F"), texts);
        Assert.IsEmpty(page.Consumed);
        await ClickDialogButtonAsync(dialog, "PrimaryButton");
        await UntilAsync(() => page.OpenDialog is null && page.Consumed.Count == 1);
        Assert.AreEqual(entry.Id, page.Consumed.Single());
        Assert.AreSequenceEqual(new[] { entry }, store.Entries);
        Assert.IsEmpty(store.Removed);
        Assert.AreEqual(0, store.ClearCount);
    }

    [UITestMethod]
    public async Task MissingNavigationRequestIsConsumedWithoutOpeningAnEmptyDialog()
    {
        await using var directory = new TemporaryDirectory();
        var store = new MemoryHistoryStore();
        var missing = Guid.NewGuid();
        await using var page = new PageFixture(directory.Path, store, missing);
        await UntilAsync(() => page.Consumed.Count == 1 && page.Texts.Contains("No receive history yet"));
        Assert.AreEqual(missing, page.Consumed.Single());
        Assert.IsNull(page.OpenDialog);
        Assert.IsEmpty(store.Removed);
        Assert.AreEqual(0, store.ClearCount);
    }

    [UITestMethod]
    public async Task ReplacingTheStoreUnsubscribesOldSourceAndUsesTheLatestDeleteCommand()
    {
        await using var directory = new TemporaryDirectory();
        var oldEntry = Entry(directory.Path, "old.txt");
        var newEntry = Entry(directory.Path, "new.txt");
        var oldStore = new MemoryHistoryStore(oldEntry);
        var newStore = new MemoryHistoryStore(newEntry);
        var page = new PageFixture(directory.Path, oldStore);
        try
        {
            await UntilAsync(() => page.RowButtons.Count == 1 && oldStore.SubscriberCount == 1);
            page.ReplaceStore(newStore);
            await UntilAsync(() => page.Texts.Contains(newEntry.FileName) && newStore.SubscriberCount == 1
                && oldStore.SubscriberCount == 0);
            Assert.IsFalse(page.Texts.Contains(oldEntry.FileName));
            await WithMenuAsync(page.RowButton(newEntry), items => Invoke(items.Single(item => item.Text == "Remove from history")));
            await UntilAsync(() => page.Texts.Contains("No receive history yet"));
            Assert.AreSequenceEqual(new[] { newEntry.Id }, newStore.Removed);
            Assert.IsEmpty(oldStore.Removed);
            Assert.AreSequenceEqual(new[] { oldEntry }, oldStore.Entries);
        }
        finally { await page.DisposeAsync(); }
        Assert.AreEqual(0, oldStore.SubscriberCount);
        Assert.AreEqual(0, newStore.SubscriberCount);
    }

    // Only the store boundary is substituted. Rendering, keyed list reconciliation,
    // commands, menus and dialogs come from the production HistoryPage. UIA Invoke
    // exercises native control wiring, not OS mouse/keyboard or shell launching.
    private sealed class PageFixture : IAsyncDisposable
    {
        private Action<IReceiveHistoryStore>? _replaceStore;
        public ReactorHostControl Host { get; } = new() { Width = 1000, Height = 640 };
        public List<Guid> Consumed { get; } = [];
        public List<string> Texts => Descendants<TextBlock>(Host).Where(IsRealized).Select(block => block.Text).ToList();
        public List<Button> RowButtons => Descendants<ItemsRepeater>(Host)
            .SelectMany(repeater => Enumerable.Range(0, repeater.ItemsSourceView?.Count ?? 0)
                .Select(repeater.TryGetElement).OfType<UIElement>())
            .SelectMany(Descendants<Button>)
            .Where(button => AutomationProperties.GetName(button).StartsWith("Actions for ", StringComparison.Ordinal)).ToList();
        public Button ClearButton => Descendants<Button>(Host).Single(button => AutomationProperties.GetName(button) == "Delete history");
        public ContentDialog? OpenDialog => Host.XamlRoot is not { } root ? null
            : VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
                .SelectMany(popup => Descendants<ContentDialog>(popup.Child)).SingleOrDefault();
        public Button RowButton(ReceiveHistoryEntry entry) => RowButtons.Single(button => AutomationProperties.GetName(button) == "Actions for " + entry.FileName);

        public PageFixture(string directory, IReceiveHistoryStore store, Guid? navigationId = null)
        {
            TestApplication.TestWindow.Content = Host;
            Host.Mount(context =>
            {
                var (currentStore, setStore) = context.UseState(store);
                var (currentNavigationId, setNavigationId) = context.UseState(navigationId);
                _replaceStore = setStore;
                return LocaleProvider("en-US", Component<HistoryPage, HistoryPageProps>(new(
                    directory, ElementTheme.Light, currentNavigationId, id =>
                    {
                        Consumed.Add(id);
                        setNavigationId(null);
                    }, currentStore)), global::Tonarink.AppShell.Resources, defaultLocale: "en-US");
            });
        }

        public void ReplaceStore(IReceiveHistoryStore store)
        {
            Assert.IsNotNull(_replaceStore);
            _replaceStore(store);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (OpenDialog is { } dialog)
                {
                    dialog.Hide();
                    await UntilAsync(() => OpenDialog is null);
                }
            }
            finally
            {
                Host.Dispose();
                TestApplication.TestWindow.Content = new Grid();
            }
            await Task.Yield();
        }
    }

    private sealed class MemoryHistoryStore(params ReceiveHistoryEntry[] entries) : IReceiveHistoryStore
    {
        private Action? _changed;
        public IReadOnlyList<ReceiveHistoryEntry> Entries { get; private set; } = entries;
        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;
        public List<Guid> Removed { get; } = [];
        public int ClearCount { get; private set; }
        public event Action? Changed { add => _changed += value; remove => _changed -= value; }
        public void Replace(params ReceiveHistoryEntry[] next) { Entries = next; _changed?.Invoke(); }
        public void Remove(Guid id) { Removed.Add(id); Replace(Entries.Where(entry => entry.Id != id).ToArray()); }
        public void Clear() { ClearCount++; Replace(); }
    }

    private static ReceiveHistoryEntry Entry(string directory, string name) =>
        new(Guid.NewGuid(), name, Path.Combine(directory, name), 123, "Owned sender", new DateTimeOffset(2026, 10, 11, 10, 30, 0, TimeSpan.Zero));

    private static async Task WithMenuAsync(Button owner, Action<MenuFlyoutItem[]> action)
    {
        Assert.IsInstanceOfType<XamlMenuFlyout>(owner.Flyout);
        var menu = (XamlMenuFlyout)owner.Flyout;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOpened(object? sender, object args) => opened.TrySetResult();
        void OnClosed(object? sender, object args) => closed.TrySetResult();
        menu.Opened += OnOpened;
        menu.Closed += OnClosed;
        try
        {
            menu.ShowAt(owner);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            action(menu.Items.OfType<MenuFlyoutItem>().ToArray());
        }
        finally
        {
            menu.Hide();
            if (opened.Task.IsCompletedSuccessfully)
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            menu.Opened -= OnOpened;
            menu.Closed -= OnClosed;
        }
    }

    private static async Task ClickDialogButtonAsync(ContentDialog dialog, string name)
    {
        await UntilAsync(() => Descendants<Button>(dialog).Any(button => button.Name == name));
        Invoke(Descendants<Button>(dialog).Single(button => button.Name == name));
    }

    private static void Invoke(Button button)
    {
        var provider = new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(provider);
        provider.Invoke();
    }

    private static void Invoke(MenuFlyoutItem item)
    {
        var provider = new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(provider);
        provider.Invoke();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static bool IsRealized(DependencyObject control)
    {
        for (var current = control; VisualTreeHelper.GetParent(current) is { } parent; current = parent)
            if (parent is ItemsRepeater repeater)
                return current is UIElement element && repeater.GetElementIndex(element) >= 0;
        return true;
    }

    private static async Task UntilAsync(Func<bool> condition, Func<string>? diagnostic = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) Assert.Fail("Timed out waiting for the real history page. " + diagnostic?.Invoke());
            await Task.Delay(25);
        }
    }
}
