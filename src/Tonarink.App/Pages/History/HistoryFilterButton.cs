using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages.History;

sealed record HistoryFilterButtonProps(HistoryFilter Filter, Action<HistoryFilter> Apply);

sealed class HistoryFilterButton : Component<HistoryFilterButtonProps>
{
    private static readonly HistoryTimeRange[] TimeRanges = Enum.GetValues<HistoryTimeRange>();
    private static readonly HistorySortOrder[] SortOrders = Enum.GetValues<HistorySortOrder>();

    public override Element Render()
    {
        var t = UseIntl();
        var (draft, setDraft) = UseState(Props.Filter);
        var buttonRef = UseRef<ToggleSplitButton?>();
        var flyoutCleanup = UseRef<Action?>();
        var alignedOpening = UseRef(false);
        var (resetVersion, setResetVersion) = UseState(0);

        var fields = VStack(16,
            Field(Message("HistoryFilterSearch"),
                TextBox(draft.Search, value => setDraft(draft with { Search = value }))
                    .PlaceholderText(Message("HistoryFilterSearchPlaceholder"))
                    .AutomationName(Message("HistoryFilterSearch"))
                    .AutomationId("History.Filter.Search")),
            Field(Message("HistoryFilterFileType"),
                Component<HistoryFileTypePicker, HistoryFileTypePickerProps>(
                    new(draft.FileTypes, types => setDraft(draft with { FileTypes = types })))),
            Field(Message("HistoryFilterTime"),
                ComboBox(TimeRanges.Select(TimeLabel).ToArray(), Array.IndexOf(TimeRanges, draft.TimeRange), index =>
                    {
                        if ((uint)index < (uint)TimeRanges.Length)
                            setDraft(draft with { TimeRange = TimeRanges[index] });
                    })
                    .AutomationName(Message("HistoryFilterTime"))
                    .AutomationId("History.Filter.Time")),
            draft.TimeRange == HistoryTimeRange.Custom
                ? VStack(8,
                    Grid(
                        columns: [GridSize.Star(), GridSize.Star()],
                        rows: [GridSize.Auto],
                        Field(Message("HistoryFilterFrom"),
                            CalendarDatePicker(ToPickerDate(draft.From), value => setDraft(draft with { From = ToDate(value) }))
                                .MinWidth(0)
                                .PlaceholderText(Message("HistoryFilterAnyDate"))
                                .AutomationName(Message("HistoryFilterFrom"))
                                .AutomationId("History.Filter.From"))
                            .MinWidth(0)
                            .Margin(right: 8)
                            .Grid(column: 0),
                        Field(Message("HistoryFilterTo"),
                            CalendarDatePicker(ToPickerDate(draft.To), value => setDraft(draft with { To = ToDate(value) }))
                                .MinWidth(0)
                                .PlaceholderText(Message("HistoryFilterAnyDate"))
                                .AutomationName(Message("HistoryFilterTo"))
                                .AutomationId("History.Filter.To"))
                            .MinWidth(0)
                            .Grid(column: 1)),
                    draft.IsValid ? null : Caption(Message("HistoryFilterInvalidRange"))
                        .Foreground(Theme.SystemCritical)
                        .TextWrapping(TextWrapping.WrapWholeWords))
                : null,
            Field(Message("HistoryFilterSort"),
                ComboBox(SortOrders.Select(SortLabel).ToArray(), Array.IndexOf(SortOrders, draft.SortOrder), index =>
                    {
                        if ((uint)index < (uint)SortOrders.Length)
                            setDraft(draft with { SortOrder = SortOrders[index] });
                    })
                    .AutomationName(Message("HistoryFilterSort"))
                    .AutomationId("History.Filter.Sort")));

        var panel = VStack(16,
                Subtitle(Message("HistoryFilterTitle")),
                ScrollView(fields)
                    .MaxHeight(400)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch),
                Grid(
                    columns: [GridSize.Star(), GridSize.Star()],
                    rows: [GridSize.Auto],
                    Button(Message("HistoryFilterApply"), () =>
                        {
                            if (!draft.IsValid) return;
                            var next = draft.Normalize();
                            Close();
                            Props.Apply(next);
                        })
                        .AccentButton()
                        .AutomationName(Message("HistoryFilterApply"))
                        .IsEnabled(draft.IsValid)
                        .HAlign(HorizontalAlignment.Stretch)
                        .AutomationId("History.Filter.Apply")
                        .Margin(right: 8)
                        .Grid(column: 0),
                    Button(Message("Cancel"), Close)
                        .AutomationName(Message("Cancel"))
                        .HAlign(HorizontalAlignment.Stretch)
                        .AutomationId("History.Filter.Cancel")
                        .Grid(column: 1)))
            .Width(320);

        return ToggleSplitButton(AppIcons.Filter, Props.Filter.IsActive, _ =>
            {
                if (Props.Filter.IsActive)
                    Props.Apply(HistoryFilter.Default);
                else
                    Open();

                // Opening the editor must not check the toggle before filters are applied.
                setResetVersion(resetVersion + 1);
            })
            .FontFamily("Segoe Fluent Icons")
            .FontSize(20)
            .Height(40)
            .Padding(12, 0)
            .AutomationName(Message("HistoryFilterTitle"))
            .AutomationId("History.Filter")
            .ToolTip(Message(Props.Filter.IsActive ? "HistoryFilterReset" : "HistoryFilterTitle"))
            .WithFlyout(ContentFlyout(panel, FlyoutPlacementMode.BottomEdgeAlignedRight))
            .OnMountAdd(element =>
            {
                if (element is not ToggleSplitButton { Flyout: { } flyout } button) return;

                buttonRef.Current = button;
                // Effects run before child mounting in Reactor. Subscribe here, after
                // WithFlyout has populated the native slot, not in a mount-only effect.
                // SplitButton forces left alignment; cancel before presentation and
                // reopen through our right-aligned path for mouse, keyboard and UIA.
                void Opening(object? sender, object args)
                {
                    if (alignedOpening.Current) return;

                    flyout.Hide();
                    button.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ReferenceEquals(buttonRef.Current, button)) Open();
                    });
                }

                // ContentFlyout has no opening callbacks in preview.16.
                void Opened(object? sender, object args) => setDraft(Props.Filter);
                flyout.Opening += Opening;
                flyout.Opened += Opened;
                flyoutCleanup.Current = () =>
                {
                    flyout.Opening -= Opening;
                    flyout.Opened -= Opened;
                };
            })
            .OnUnmountAdd(_ =>
            {
                flyoutCleanup.Current?.Invoke();
                flyoutCleanup.Current = null;
                buttonRef.Current = null;
            });

        string Message(string key) => t.Message(new("App", key));

        void Open()
        {
            if (buttonRef.Current is not { } button) return;

            alignedOpening.Current = true;
            try
            {
                button.Flyout?.ShowAt(button, new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
                });
            }
            finally
            {
                alignedOpening.Current = false;
            }
        }

        string TimeLabel(HistoryTimeRange range) => Message(range switch
        {
            HistoryTimeRange.All => "HistoryFilterTimeAll",
            HistoryTimeRange.Today => "HistoryFilterTimeToday",
            HistoryTimeRange.LastSevenDays => "HistoryFilterTimeWeek",
            HistoryTimeRange.LastThirtyDays => "HistoryFilterTimeMonth",
            HistoryTimeRange.Custom => "HistoryFilterTimeCustom",
        });

        string SortLabel(HistorySortOrder order) => Message(order switch
        {
            HistorySortOrder.Newest => "HistoryFilterSortNewest",
            HistorySortOrder.Oldest => "HistoryFilterSortOldest",
            HistorySortOrder.NameAscending => "HistoryFilterSortNameAscending",
            HistorySortOrder.NameDescending => "HistoryFilterSortNameDescending",
            HistorySortOrder.SizeAscending => "HistoryFilterSortSizeAscending",
            HistorySortOrder.SizeDescending => "HistoryFilterSortSizeDescending",
        });

        // WithFlyout reconciles the panel in place in the SplitButton's own Flyout slot.
        void Close() => buttonRef.Current?.Flyout?.Hide();
    }

    private static Element Field(string label, Element control) =>
        VStack(6, Caption(label).Foreground(Theme.SecondaryText), control.HAlign(HorizontalAlignment.Stretch));

    private static DateTimeOffset? ToPickerDate(DateOnly? date) => date is { } value
        ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)) : null;

    private static DateOnly? ToDate(DateTimeOffset? date) => date is { } value
        ? DateOnly.FromDateTime(value.DateTime) : null;
}
