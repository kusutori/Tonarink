using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed record ShellTitleBarProps(
    NavigationHandle<AppRoute> Navigation,
    bool PaneToggleVisible,
    Action TogglePane);

sealed class ShellTitleBar : Component<ShellTitleBarProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return TitleBar("Tonarink")
            .Subtitle(t.Message(new("App", "Tagline")))
            .WithNavigation(Props.Navigation)
            .PaneToggleButtonVisible(Props.PaneToggleVisible)
            .PaneToggleRequested(Props.TogglePane)
            .Tall()
            .Flex(shrink: 0);
    }
}
