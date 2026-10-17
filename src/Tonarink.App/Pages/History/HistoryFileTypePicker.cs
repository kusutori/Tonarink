using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using static Tonarink.Controls.TokenizingTextBoxElement;

namespace Tonarink.Pages.History;

sealed record HistoryFileTypePickerProps(
    ImmutableHashSet<FileTypeGlyphKind> Selected,
    Action<ImmutableHashSet<FileTypeGlyphKind>> Changed);

sealed class HistoryFileTypePicker : Component<HistoryFileTypePickerProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var state = UseMemo(() => new PickerState(Enum.GetValues<FileTypeGlyphKind>()
            .Select(kind => new FileTypeToken(kind, t.Message(new("App", LabelKey(kind)))))
            .ToArray()), t.Locale);
        state.Changed = Props.Changed;
        UseEffect(() => state.Sync(Props.Selected), Props.Selected, state);

        return (TokenizingTextBox(
            itemsSource: state.Items,
            suggestedItemsSource: state.Suggestions,
            textMemberPath: nameof(FileTypeToken.Label),
            // Labels contain spaces in several locales; typing one must not
            // split a search into free-form tokens.
            tokenDelimiter: string.Empty,
            maximumTokens: state.Options.Length,
            placeholderText: t.Message(new("App", "HistoryFilterTypeAll"))) with
            {
                OnTextChanged = _ => state.RefreshSuggestions(),
                OnTokenItemAdding = state.ValidateToken,
                OnTokenItemAdded = _ => state.SelectionChanged(),
                OnTokenItemRemoved = _ => state.SelectionChanged(),
            })
            .AutomationName(t.Message(new("App", "HistoryFilterFileType")))
            .AutomationId("History.Filter.FileType")
            .MinWidth(0)
            .OnGotFocus((_, args) => state.OpenSuggestions(args.OriginalSource))
            // A second click should reopen suggestions after Escape, even if
            // the input already has focus and therefore raises no GotFocus.
            .OnPointerReleased((_, args) => state.OpenSuggestions(args.OriginalSource))
            .Set(control =>
            {
                state.Control = control;
                // The generated wrapper doesn't surface DataTemplate properties.
                if (!ReferenceEquals(control.TokenItemTemplate, state.Template))
                    control.TokenItemTemplate = state.Template;
                if (!ReferenceEquals(control.SuggestedItemTemplate, state.Template))
                    control.SuggestedItemTemplate = state.Template;
            })
            .OnUnmountAdd(_ => state.Control = null);
    }

    private static string LabelKey(FileTypeGlyphKind kind) => kind switch
    {
        FileTypeGlyphKind.Document => "HistoryFilterTypeDocuments",
        FileTypeGlyphKind.Folder => "HistoryFilterTypeFolders",
        FileTypeGlyphKind.Archive => "HistoryFilterTypeArchives",
        FileTypeGlyphKind.Pdf => "HistoryFilterTypePdf",
        FileTypeGlyphKind.Text => "HistoryFilterTypeText",
        FileTypeGlyphKind.Video => "HistoryFilterTypeVideos",
        FileTypeGlyphKind.Image => "HistoryFilterTypeImages",
        FileTypeGlyphKind.Audio => "HistoryFilterTypeAudio",
        FileTypeGlyphKind.Email => "HistoryFilterTypeEmail",
        FileTypeGlyphKind.Calendar => "HistoryFilterTypeCalendar",
        FileTypeGlyphKind.OfflineMap => "HistoryFilterTypeOfflineMaps",
        FileTypeGlyphKind.Database => "HistoryFilterTypeDatabases",
        FileTypeGlyphKind.Font => "HistoryFilterTypeFonts",
        FileTypeGlyphKind.Ebook => "HistoryFilterTypeEbooks",
        FileTypeGlyphKind.Contact => "HistoryFilterTypeContacts",
        FileTypeGlyphKind.Subtitle => "HistoryFilterTypeSubtitles",
        FileTypeGlyphKind.TextMessage => "HistoryFilterTypeTextMessages",
        FileTypeGlyphKind.Clipboard => "HistoryFilterTypeClipboard",
    };

    private sealed class PickerState(FileTypeToken[] options)
    {
        public FileTypeToken[] Options { get; } = options;
        // Keep the same notifying sources throughout a mounted picker's lifetime.
        // ListViewBase and the toolkit's editor manage their own native containers.
        public ObservableCollection<object> Items { get; } = [];
        public ObservableCollection<object> Suggestions { get; } = [];
        // WASDK 2.5 Experimental creates each template visual directly from C#.
        // Reuse the template, not its visuals: containers may realize/recycle it
        // independently, with WinUI supplying their current DataContext.
        public DataTemplate Template { get; } = new(static () =>
        {
            var icon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16,
            };
            icon.SetBinding(FontIcon.GlyphProperty, new Binding
            {
                Path = new PropertyPath(nameof(FileTypeToken.Glyph)),
            });
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath(nameof(FileTypeToken.Label)),
            });
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(icon);
            content.Children.Add(label);
            return content;
        });
        public TokenizingTextBox? Control { get; set; }
        public Action<ImmutableHashSet<FileTypeGlyphKind>>? Changed { get; set; }

        public void OpenSuggestions(object source)
        {
            if (Control is not { } owner)
                return;

            // The toolkit does not expose its active AutoSuggestBox. Resolve
            // only the event source's ancestor chain, stopping at this picker;
            // token/removal-button clicks must not open the editor's popup.
            var input = FindInput(source as DependencyObject, owner);
            if (input is null)
                return;

            // Let the native input finish handling focus/tap before opening.
            // Recheck focus so a queued callback cannot reopen a closed flyout.
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(Control, owner) || owner.XamlRoot is not { } root
                    || !ReferenceEquals(FindInput(FocusManager.GetFocusedElement(root) as DependencyObject, owner), input))
                    return;

                RefreshSuggestions();
                input.IsSuggestionListOpen = Suggestions.Count != 0;
            });
        }

        private static AutoSuggestBox? FindInput(DependencyObject? source, TokenizingTextBox owner)
        {
            AutoSuggestBox? input = null;
            for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, owner))
                    return input;
                if (current is AutoSuggestBox suggestBox)
                    input = suggestBox;
            }

            return null;
        }

        public void Sync(ImmutableHashSet<FileTypeGlyphKind> selected)
        {
            for (var index = Items.Count - 1; index >= 0; index--)
                if (Items[index] is not FileTypeToken token || !selected.Contains(token.Kind))
                    Items.RemoveAt(index);

            foreach (var option in Options)
                if (selected.Contains(option.Kind) && !Items.Contains(option))
                    Items.Add(option);

            RefreshSuggestions();
        }

        public void ValidateToken(TokenItemAddingEventArgs args)
        {
            // Only known types can become tokens; free-form input is a search,
            // not a new file type. Exact labels may also be submitted with Enter.
            var option = Options.FirstOrDefault(option => string.Equals(
                option.Label, args.TokenText.Trim(), StringComparison.CurrentCultureIgnoreCase));
            args.Cancel = option is null || Items.Contains(option);
            if (!args.Cancel)
                args.Item = option;
        }

        public void SelectionChanged()
        {
            // Suggestion submission bypasses TokenItemAdding in the toolkit.
            // Defensively reject duplicates here as well as hiding them below.
            var selected = ImmutableHashSet.CreateBuilder<FileTypeGlyphKind>();
            for (var index = 0; index < Items.Count; index++)
            {
                if (Items[index] is FileTypeToken token && selected.Add(token.Kind))
                    continue;
                Items.RemoveAt(index--);
            }

            RefreshSuggestions();
            Changed?.Invoke(selected.ToImmutable());
        }

        public void RefreshSuggestions()
        {
            var query = Control?.Text?.Trim() ?? "";
            var matching = Options.Where(option => !Items.Contains(option)
                && option.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if (Suggestions.SequenceEqual(matching))
                return;

            Suggestions.Clear();
            foreach (var option in matching)
                Suggestions.Add(option);
        }
    }
}
