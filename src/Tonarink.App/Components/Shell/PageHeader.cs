using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private const double TitleLineHeight = 36;
    private static readonly FontWeight TitleWeight = new(700);

    public override Element Render()
    {
        var t = UseIntl();
        var navigation = Props.Navigation;
        var route = Props.Route;
        var layoutHandler = UseRef<EventHandler<object>?>();
        var (title, parentRoute, parentTitle) = CrumbLabels(t, route);
        var parentKey = parentRoute is { } parent ? $"{parent}|{parentTitle}" : "";
        var items = UseMemo(
            () => parentRoute is { } parent && !string.IsNullOrEmpty(parentTitle)
                ? new[] { Breadcrumb(parentTitle, parent), Breadcrumb(title, route) }
                : new[] { Breadcrumb(title, route) },
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
                breadcrumb.Resources["BreadcrumbBarChevronPadding"] = new Thickness(8, 0, 8, 0);
            })
            .OnMountAdd(element =>
            {
                if (element is not BreadcrumbBar breadcrumb)
                    return;

                EventHandler<object> handler = (_, _) => ApplyHeadingMetrics(breadcrumb);
                layoutHandler.Current = handler;
                breadcrumb.LayoutUpdated += handler;
                handler(breadcrumb, EventArgs.Empty);
            })
            .OnUnmountAdd(element =>
            {
                if (element is not BreadcrumbBar breadcrumb)
                    return;
                if (layoutHandler.Current is not { } handler)
                    return;

                breadcrumb.LayoutUpdated -= handler;
                layoutHandler.Current = null;
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
            _ => (t.Message(new("App", "ReceiveTitle")), null, null),
        };

    private static void ApplyHeadingMetrics(DependencyObject root)
    {
        foreach (var presenter in FindAll<ContentPresenter>(root))
        {
            if (presenter.Name is not (
                "PART_ItemContentPresenter" or
                "PART_LastItemContentPresenter" or
                "PART_EllipsisDropDownItemContentPresenter"))
                continue;

            presenter.FontSize = TitleFontSize;
            presenter.FontWeight = TitleWeight;
            presenter.LineHeight = TitleLineHeight;
            presenter.Padding = new Thickness(0);
        }

        foreach (var item in FindAll<BreadcrumbBarItem>(root))
        {
            item.FontSize = TitleFontSize;
            item.FontWeight = TitleWeight;
            item.Padding = new Thickness(0);
            if (FindNamed<Button>(item, "PART_ItemButton") is { } button)
                button.Padding = new Thickness(0);
            if (FindNamed<ContentPresenter>(item, "PART_LastItemContentPresenter") is
                { Visibility: Visibility.Visible })
                AutomationProperties.SetHeadingLevel(item, AutomationHeadingLevel.Level1);
        }
    }

    private static T? FindNamed<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        foreach (var match in FindAll<T>(root))
        {
            if (match.Name == name)
                return match;
        }

        return null;
    }

    private static List<T> FindAll<T>(DependencyObject root)
        where T : class
    {
        var results = new List<T>();
        Collect(root, results);
        return results;

        static void Collect(DependencyObject node, List<T> found)
        {
            if (node is T match)
                found.Add(match);

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
                Collect(VisualTreeHelper.GetChild(node, index), found);
        }
    }
}
