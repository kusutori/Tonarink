using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Windows.UI.Text;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed record PageHeaderProps(
    AppRoute Route,
    NavigationHandle<AppRoute> Navigation,
    Element? RightContent = null);

sealed class PageHeaderRightSlot
{
    public AppRoute? Owner { get; set; }
    public Func<Element>? Build { get; set; }
    public required Action Invalidate { get; init; }
}

sealed class PageHeader : Component<PageHeaderProps>
{
    internal static readonly Context<PageHeaderRightSlot> RightSlot =
        new(new PageHeaderRightSlot { Invalidate = static () => { } });

    private const double TitleFontSize = 28;
    private static readonly FontWeight TitleWeight = new(700);

    public override Element Render()
    {
        var t = UseIntl();
        var navigation = Props.Navigation;
        var route = Props.Route;
        var (title, parentRoute, parentTitle) = CrumbLabels(t, route);
        var parentKey = parentRoute is not null ? $"{parentRoute}|{parentTitle}" : "";
        var items = UseMemo<BreadcrumbBarItemData[]>(
            () => parentRoute is { } parent && !string.IsNullOrEmpty(parentTitle)
                ? [Breadcrumb(parentTitle, parent), Breadcrumb(title, route)]
                : [Breadcrumb(title, route)],
            title,
            route,
            parentKey);

        var bar = BreadcrumbBar(items, item =>
            {
                if (item.Tag is not AppRoute target || Equals(target, navigation.CurrentRoute))
                    return;
                GoTo(navigation, target);
            })
            .Resources(static resources => resources
                .Set("BreadcrumbBarItemThemeFontSize", TitleFontSize)
                .Set("BreadcrumbBarChevronFontSize", 16d))
            .Set(static breadcrumb =>
            {
                breadcrumb.Resources["BreadcrumbBarItemFontWeight"] = TitleWeight;
                breadcrumb.Resources["BreadcrumbBarChevronPadding"] = new Thickness(8, 4, 8, 0);
            })
            .AutomationName(title)
            .MinHeight(AppLayout.PageHeaderRowMinHeight)
            .VAlign(VerticalAlignment.Center);

        return Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            bar.Grid(column: 0).VAlign(VerticalAlignment.Center),
            (Props.RightContent ?? Empty())
            .MinHeight(AppLayout.PageHeaderRowMinHeight)
            .VAlign(VerticalAlignment.Center)
            .Margin(left: Props.RightContent is null ? 0 : 8)
            .Grid(column: 1));
    }

    private static void GoTo(NavigationHandle<AppRoute> navigation, AppRoute target)
    {
        if (Equals(target, navigation.CurrentRoute))
            return;
        if (!navigation.PopTo(candidate => Equals(candidate, target)))
            navigation.Reset(target);
    }

    private static (string Title, AppRoute? Parent, string? ParentTitle) CrumbLabels(
        IntlAccessor t,
        AppRoute route) =>
        route switch
        {
            AppRoute.History => (
                t.Message(new("App", "HistoryTitle")),
                AppRoute.Receive,
                t.Message(new("App", "NavReceive"))),
            AppRoute.WebReceive => (
                t.Message(new("App", "WebReceiveTitle")),
                AppRoute.Receive,
                t.Message(new("App", "NavReceive"))),
            AppRoute.WebShare => (
                t.Message(new("App", "WebShareTitle")),
                AppRoute.Send,
                t.Message(new("App", "NavSend"))),
            AppRoute.DeviceDetails => (
                t.Message(new("App", "DeviceDetailsTitle")),
                AppRoute.Send,
                t.Message(new("App", "NavSend"))),
            AppRoute.NetworkInterfaces => (
                t.Message(new("App", "SettingsNetworkInterfaces")),
                AppRoute.Settings,
                t.Message(new("App", "NavSettings"))),
            AppRoute.Send => (t.Message(new("App", "SendTitle")), null, null),
            AppRoute.Settings => (t.Message(new("App", "SettingsTitle")), null, null),
            AppRoute.Receive => (t.Message(new("App", "ReceiveTitle")), null, null),
        };
}
