using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using static Tonarink.Controls.TokenizingTextBoxElement;
using XamlControl = Microsoft.UI.Xaml.Controls.Control;

namespace Tonarink.Pages.History;

sealed record HistoryFileTypePickerProps(
    ImmutableHashSet<FileTypeGlyphKind> Selected,
    Action<ImmutableHashSet<FileTypeGlyphKind>> Changed);

sealed class HistoryFileTypePicker : Component<HistoryFileTypePickerProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var state = UseMemo(() => new PickerState([
            .. HistoryFilter.AvailableFileTypes
                .Select(kind => new FileTypeToken(kind, t.Message(new("App", LabelKey(kind)))))
        ], t.Message(new("App", "Remove")), t.Message(new("App", "SelectAll"))), t.Locale);
        state.Changed = Props.Changed;
        UseEffect(() => state.DetachControl, state);
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
            OnTokenItemAdding = state.ValidateToken,
            OnTokenItemAdded = _ => state.SelectionChanged(),
            OnTokenItemRemoved = _ => state.SelectionChanged(),
        })
            .AutomationName(t.Message(new("App", "HistoryFilterFileType")))
            .AutomationId("History.Filter.FileType")
            .MinWidth(0)
            .OnGotFocus((_, args) =>
            {
                state.LocalizeContextMenu(args.OriginalSource);
                state.OpenSuggestions(args.OriginalSource);
            })
            .OnLostFocus((_, _) => state.FocusLeftPicker())
            .OnRightTapped((_, args) => state.LocalizeContextMenu(args.OriginalSource))
            .Set(control =>
            {
                state.AttachControl(control);
                // The generated wrapper doesn't surface DataTemplate properties.
                if (!ReferenceEquals(control.TokenItemTemplate, state.Template))
                    control.TokenItemTemplate = state.Template;
                if (!ReferenceEquals(control.SuggestedItemTemplate, state.SuggestionTemplate))
                    control.SuggestedItemTemplate = state.SuggestionTemplate;
                if (!ReferenceEquals(control.SuggestedItemContainerStyle, state.SuggestionContainerStyle))
                    control.SuggestedItemContainerStyle = state.SuggestionContainerStyle;
            })
            .OnUnmountAdd(_ => state.DetachControl());
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

    private sealed class PickerState(FileTypeToken[] options, string removeText, string selectAllText)
    {
        private int _selectionVersion;
        private bool _counterThemeQueued;
        private long _enabledChangedToken;
        private bool _dismissEmptySuggestions;
        private PointerEventHandler? _inputPointerHandler;

        public FileTypeToken[] Options { get; } = options;

        // Keep the same notifying sources throughout a mounted picker's lifetime.
        // ListViewBase and the toolkit's editor manage their own native containers.
        public ObservableCollection<object> Items { get; } = [];

        public ObservableCollection<object> Suggestions { get; } = [];

        // WASDK 2.5 Experimental creates each template visual directly from C#.
        // Reuse the template, not its visuals: containers may realize/recycle it
        // independently, with WinUI supplying their current DataContext.
        public DataTemplate Template { get; } = new(static () => CreateTemplateContent(showExtension: false));

        public DataTemplate SuggestionTemplate { get; } = new(static () => CreateTemplateContent(showExtension: true));

        // Stretch the container as well as the template, so its star column
        // places the suffix at the popup's right edge rather than after the name.
        public Style SuggestionContainerStyle { get; } = new(typeof(ListViewItem))
        {
            Setters = { new Setter(XamlControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) },
        };

        private static FrameworkElement CreateTemplateContent(bool showExtension)
        {
            var icon = new FontIcon
            {
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
            if (!showExtension)
            {
                var token = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                token.Children.Add(icon);
                token.Children.Add(label);
                return token;
            }

            var extension = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            extension.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath(nameof(FileTypeToken.Extension)),
            });
            extension.SetThemeResourceBinding(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.Margin = new Thickness(6, 0, 0, 0);
            var content = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(label, 1);
            Grid.SetColumn(extension, 2);
            content.Children.Add(icon);
            content.Children.Add(label);
            content.Children.Add(extension);
            return content;
        }

        private TokenizingTextBox? Control { get; set; }
        public Action<ImmutableHashSet<FileTypeGlyphKind>>? Changed { get; set; }

        public void AttachControl(TokenizingTextBox control)
        {
            if (!ReferenceEquals(Control, control))
            {
                DetachControl();
                Control = control;
                control.Loaded += ControlLoaded;
                // WCT forwards this event with its inner AutoSuggestBox as the
                // sender. Generated event trampolines look up the Reactor tag
                // on that sender, not this wrapped TokenizingTextBox.
                control.TextChanged += ControlTextChanged;
                control.QuerySubmitted += ControlQuerySubmitted;
                // The inner TextBox handles pointer events. Reactor's ordinary
                // routed callbacks don't see them, so opt into handled events
                // here to reopen the list on a second click, without refocusing.
                _inputPointerHandler = (_, args) => OpenSuggestions(args.OriginalSource, fromPointer: true);
                control.AddHandler(UIElement.PointerPressedEvent, _inputPointerHandler, handledEventsToo: true);
                // Reactor does not currently expose a ContextRequested modifier.
                control.ContextRequested += ControlContextRequested;
                _enabledChangedToken = control.RegisterPropertyChangedCallback(
                    XamlControl.IsEnabledProperty, (_, _) => QueueCounterTheme());
            }

            if (control.IsLoaded)
                QueueCounterTheme();
        }

        public void DetachControl()
        {
            if (Control is not { } control)
                return;

            control.Loaded -= ControlLoaded;
            control.TextChanged -= ControlTextChanged;
            control.QuerySubmitted -= ControlQuerySubmitted;
            control.RemoveHandler(UIElement.PointerPressedEvent, _inputPointerHandler);
            _inputPointerHandler = null;
            control.ContextRequested -= ControlContextRequested;
            control.UnregisterPropertyChangedCallback(XamlControl.IsEnabledProperty, _enabledChangedToken);
            Control = null;
            _dismissEmptySuggestions = false;
        }

        private void ControlLoaded(object sender, RoutedEventArgs args)
        {
            LocalizeContextMenu(sender);
            QueueCounterTheme();
        }

        private void ControlContextRequested(UIElement sender, ContextRequestedEventArgs args) =>
            LocalizeContextMenu(args.OriginalSource);

        private void ControlTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && sender.Text.Length != 0)
                _dismissEmptySuggestions = false;

            RefreshSuggestions(sender.Text);
            // WCT clears the input and restores focus after submitting. WinUI
            // may dispatch several text/focus events later; none should reopen
            // the full list until the user types or explicitly clicks again.
            if (_dismissEmptySuggestions && sender.Text.Length == 0)
            {
                sender.IsSuggestionListOpen = false;
                // Collection notifications can make AutoSuggestBox reopen its
                // popup after this handler returns (especially 0 -> N results).
                var owner = Control;
                owner?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (ReferenceEquals(Control, owner) && _dismissEmptySuggestions && sender.Text.Length == 0)
                        sender.IsSuggestionListOpen = false;
                });
            }
        }

        private void ControlQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            // Also cancel a focus/tap-open queued before an unsuccessful
            // submission: no TokenItemAdded event will invalidate it then.
            _selectionVersion++;
            _dismissEmptySuggestions = true;
            sender.IsSuggestionListOpen = false;
        }

        public void FocusLeftPicker()
        {
            if (Control is not { XamlRoot: { } root } owner)
                return;

            for (var current = FocusManager.GetFocusedElement(root) as DependencyObject;
                 current is not null;
                 current = VisualTreeHelper.GetParent(current))
                if (ReferenceEquals(current, owner))
                    return;

            // Returning with Tab should show suggestions too. Internal focus
            // restoration during token submission must retain the dismissal.
            _dismissEmptySuggestions = false;
        }

        public void LocalizeContextMenu(object source)
        {
            if (Control is not { } owner)
                return;

            for (var current = source as DependencyObject;
                 current is not null;
                 current = VisualTreeHelper.GetParent(current))
            {
                // WCT 8.2 creates [Remove, Select all] for token containers and
                // [Select all] for the picker itself, with hard-coded English text.
                // Change only those labels, retaining the toolkit's click handlers.
                if (current is TokenizingTextBoxItem { ContextFlyout: MenuFlyout menu })
                {
                    if (menu.Items.Count == 2 && menu.Items[0] is MenuFlyoutItem remove
                                              && menu.Items[1] is MenuFlyoutItem selectAll)
                    {
                        remove.Text = removeText;
                        selectAll.Text = selectAllText;
                    }

                    return;
                }

                if (ReferenceEquals(current, owner))
                {
                    if (owner.ContextFlyout is MenuFlyout { Items: [MenuFlyoutItem selectAll] })
                        selectAll.Text = selectAllText;
                    return;
                }
            }
        }

        private void QueueCounterTheme()
        {
            if (_counterThemeQueued || Control is not { } owner)
                return;

            _counterThemeQueued = true;
            if (!owner.DispatcherQueue.TryEnqueue(() =>
                {
                    _counterThemeQueued = false;
                    if (!ReferenceEquals(Control, owner) || !owner.IsLoaded)
                        return;

                    // WCT 8.2 overwrites the counter's ThemeResource with a brush
                    // fetched from Application.Resources on every count change.
                    // Repair only this documented template part, after its handlers
                    // finish, using the element's actual theme rather than the app's.
                    var counter = owner.FindDescendant<TextBlock>(static text =>
                        text is { Name: "PART_TokensCounter", Visibility: Visibility.Visible });
                    counter?.SetThemeResourceBinding(TextBlock.ForegroundProperty,
                        !owner.IsEnabled ? "TextFillColorDisabledBrush"
                        : Items.Count >= Options.Length ? "SystemFillColorCriticalBrush"
                        : "TextFillColorSecondaryBrush");
                }))
                _counterThemeQueued = false;
        }

        public void OpenSuggestions(object source, bool fromPointer = false)
        {
            if (Control is not { } owner)
                return;

            // The toolkit does not expose its active AutoSuggestBox. Resolve
            // only the event source's ancestor chain, stopping at this picker;
            // token/removal-button clicks must not open the editor's popup.
            var input = FindInput(source as DependencyObject, owner);
            if (input is null)
                return;

            if (fromPointer)
                _dismissEmptySuggestions = false;
            else if (_dismissEmptySuggestions)
                return;

            var selectionVersion = _selectionVersion;

            // Let the native input finish handling focus/tap before opening.
            // Recheck focus so a queued callback cannot reopen a closed flyout.
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                if (_dismissEmptySuggestions || selectionVersion != _selectionVersion
                                             || !ReferenceEquals(Control, owner) || owner.XamlRoot is not { } root
                                             || !ReferenceEquals(
                                                 FindInput(FocusManager.GetFocusedElement(root) as DependencyObject,
                                                     owner), input))
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
            // not a new file type. Enter completes the best remaining match,
            // using the same ordering as the suggestions (including suffixes).
            var query = args.TokenText.Trim();
            var option = query.Length == 0 ? null : MatchingOptions(query).FirstOrDefault();
            args.Cancel = option is null;
            if (!args.Cancel)
                args.Item = option;
        }

        public void SelectionChanged()
        {
            // Native token submission restores editor focus before raising this
            // event. Invalidate any open queued by that focus change: otherwise
            // it can reopen the popup immediately after submission closes it.
            _selectionVersion++;
            QueueCounterTheme();

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

        private IEnumerable<FileTypeToken> MatchingOptions(string query) => Options
            .Where(option => !Items.Contains(option))
            .Select(option => (Option: option, Rank: MatchRank(option, query)))
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank)
            .Select(match => match.Option);

        private static int MatchRank(FileTypeToken option, string query)
        {
            if (query.Length == 0)
                return 0;
            if (string.Equals(option.Label, query, StringComparison.CurrentCultureIgnoreCase))
                return 0;

            var extensions = FileTypeGlyphs.ExtensionsForKind(option.Kind);
            if (extensions.Any(extension => string.Equals(extension.TrimStart('.'),
                    query.TrimStart('.'), StringComparison.OrdinalIgnoreCase)))
                return 1;
            if (option.Label.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
                return 2;
            if (option.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                return 3;
            if (extensions.Any(extension => extension.Contains(query, StringComparison.OrdinalIgnoreCase)))
                return 4;
            return -1;
        }

        private void RefreshSuggestions(string? text = null)
        {
            var query = (text ?? Control?.Text ?? "").Trim();
            var matching = MatchingOptions(query).ToArray();
            // Keep surviving suggestion containers alive. Clear() raises Reset
            // and briefly empties the popup on every token add/remove. Establish
            // the new prefix first so a nonempty result never passes through an
            // artificial empty state, even when the query changes completely.
            for (var index = 0; index < matching.Length; index++)
            {
                var option = matching[index];
                if (index < Suggestions.Count && ReferenceEquals(Suggestions[index], option))
                    continue;

                var previousIndex = Suggestions.IndexOf(option);
                if (previousIndex >= 0)
                    Suggestions.Move(previousIndex, index);
                else
                    Suggestions.Insert(index, option);
            }

            while (Suggestions.Count > matching.Length)
                Suggestions.RemoveAt(Suggestions.Count - 1);
        }
    }
}
