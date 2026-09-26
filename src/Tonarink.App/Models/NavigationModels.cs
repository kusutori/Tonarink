using Microsoft.UI.Reactor.Navigation;

namespace Tonarink.Models;

enum AppRoute
{
    Receive,
    History,
    Send,
    Settings,
    NetworkInterfaces,
    WebShare,
    WebReceive,
    DeviceDetails,
}

static class AppNavigation
{
    public static readonly NavigateOptions DrillIn = new()
    {
        Transition = NavigationTransition.DrillIn(),
    };

    public static bool IsDetail(AppRoute route) => route is
        AppRoute.History or AppRoute.NetworkInterfaces or AppRoute.WebShare or AppRoute.WebReceive
        or AppRoute.DeviceDetails;
}
