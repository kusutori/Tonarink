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
    private static readonly HistoryFileType[] FileTypes = Enum.GetValues<HistoryFileType>();
    private static readonly HistoryTimeRange[] TimeRanges = Enum.GetValues<HistoryTimeRange>();
    private static readonly HistorySortOrder[] SortOrders = Enum.GetValues<HistorySortOrder>();

    public override Element Render()
    {
        var t = UseIntl();
        var (draft, setDraft) = UseState(Props.Filter);
        var flyoutRef = UseRef<Flyout?>();

        var trigger = Button(Icon(AppIcons.Filter).AccessibilityHidden())
            .Size(40, 40)
            .Padding(0, 0)
            .AutomationName(Message("HistoryFilterTitle"))
            .AutomationId("History.Filter")
            .ToolTip(Message("HistoryFilterTitle"));
        trigger = Props.Filter.IsActive ? trigger.AccentButton() : trigger.SubtleButton();

        var fields = VStack(16,
            Field(Message("HistoryFilterSearch"),
                TextBox(draft.Search, value => setDraft(draft with { Search = value }))
                    .PlaceholderText(Message("HistoryFilterSearchPlaceholder"))
                    .AutomationName(Message("HistoryFilterSearch"))
                    .AutomationId("History.Filter.Search")),
            Field(Message("HistoryFilterFileType"),
                ComboBox(FileTypes.Select(TypeLabel).ToArray(), Array.IndexOf(FileTypes, draft.FileType), index =>
                    {
                        if ((uint)index < (uint)FileTypes.Length)
                            setDraft(draft with { FileType = FileTypes[index] });
                    })
                    .AutomationName(Message("HistoryFilterFileType"))
                    .AutomationId("History.Filter.FileType")),
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
                    Field(Message("HistoryFilterFrom"),
                        CalendarDatePicker(ToPickerDate(draft.From), value => setDraft(draft with { From = ToDate(value) }))
                            .PlaceholderText(Message("HistoryFilterAnyDate"))
                            .AutomationName(Message("HistoryFilterFrom"))
                            .AutomationId("History.Filter.From")),
                    Field(Message("HistoryFilterTo"),
                        CalendarDatePicker(ToPickerDate(draft.To), value => setDraft(draft with { To = ToDate(value) }))
                            .PlaceholderText(Message("HistoryFilterAnyDate"))
                            .AutomationName(Message("HistoryFilterTo"))
                            .AutomationId("History.Filter.To")),
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
                Button(Message("HistoryFilterReset"), () => setDraft(HistoryFilter.Default))
                    .SubtleButton()
                    .AutomationName(Message("HistoryFilterReset"))
                    .HAlign(HorizontalAlignment.Left)
                    .AutomationId("History.Filter.Reset"),
                Grid(
                    columns: [GridSize.Star(), GridSize.Star()],
                    rows: [GridSize.Auto],
                    Button(Message("HistoryFilterApply"), () =>
                        {
                            if (!draft.IsValid) return;
                            Props.Apply(draft.Normalize());
                            Close();
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

        return (Flyout(trigger, panel) with { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight })
            .Opened(() => setDraft(Props.Filter))
            .Set(flyout => flyoutRef.Current = flyout)
            .OnUnmountAdd(_ => flyoutRef.Current = null);

        string Message(string key) => t.Message(new("App", key));

        string TypeLabel(HistoryFileType type) => Message(type switch
        {
            HistoryFileType.All => "HistoryFilterTypeAll",
            HistoryFileType.Document => "HistoryFilterTypeDocuments",
            HistoryFileType.Image => "HistoryFilterTypeImages",
            HistoryFileType.Video => "HistoryFilterTypeVideos",
            HistoryFileType.Audio => "HistoryFilterTypeAudio",
            HistoryFileType.Archive => "HistoryFilterTypeArchives",
            HistoryFileType.Other => "HistoryFilterTypeOther",
        });

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

        // preview.16 exposes native opening and Opened, but IsOpen=false does
        // not hide this flyout. Keep only the close operation in this escape hatch.
        void Close() => flyoutRef.Current?.Hide();
    }

    private static Element Field(string label, Element control) =>
        VStack(6, Caption(label).Foreground(Theme.SecondaryText), control.HAlign(HorizontalAlignment.Stretch));

    private static DateTimeOffset? ToPickerDate(DateOnly? date) => date is { } value
        ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)) : null;

    private static DateOnly? ToDate(DateTimeOffset? date) => date is { } value
        ? DateOnly.FromDateTime(value.DateTime) : null;
}
