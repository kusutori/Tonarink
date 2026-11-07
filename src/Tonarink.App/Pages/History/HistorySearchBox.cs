using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages.History;

sealed record HistorySearchBoxProps(HistoryFilter Filter, Action<HistoryFilter> Changed);

sealed class HistorySearchBox : Component<HistorySearchBoxProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var filter = Props.Filter;

        return VStack(6,
            Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                TextBox(filter.Search, value => Props.Changed(filter with { Search = value }))
                    .PlaceholderText(Message("HistoryFilterSearchPlaceholder"))
                    .MinWidth(0)
                    .MinHeight(40)
                    .Padding(12, 8)
                    .VerticalContentAlignment(VerticalAlignment.Center)
                    .AutomationName(Message("HistoryFilterSearch"))
                    .AutomationId("History.Filter.Search")
                    .Grid(column: 0),
                HStack(4,
                        Option("Aa", "HistoryFilterMatchCase", "MatchCase", filter.MatchCase,
                            value => Props.Changed(filter with { MatchCase = value })),
                        Option("ab", "HistoryFilterWholeWord", "WholeWord", filter.WholeWord,
                            value => Props.Changed(filter with { WholeWord = value })),
                        Option(".*", "HistoryFilterRegex", "Regex", filter.UseRegex,
                            value => Props.Changed(filter with { UseRegex = value })))
                    .Margin(left: 8)
                    .VAlign(VerticalAlignment.Stretch)
                    .Grid(column: 1)),
            filter.IsSearchValid
                ? null
                : Caption(Message("HistoryFilterInvalidRegex"))
                    .Foreground(Theme.SystemCritical)
                    .TextWrapping(TextWrapping.WrapWholeWords)
                    .LiveRegion());

        string Message(string key) => t.Message(new("App", key));

        Element Option(string label, string key, string id, bool selected, Action<bool> changed) =>
            ToggleButton(label, selected, changed)
                .MinWidth(28)
                .MinHeight(40)
                .Padding(4, 0)
                .VAlign(VerticalAlignment.Stretch)
                .FontSize(12)
                .AutomationName(Message(key))
                .ToolTip(Message(key))
                .AutomationId($"History.Filter.{id}");
    }
}
