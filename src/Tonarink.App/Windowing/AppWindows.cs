using Microsoft.UI.Reactor;

namespace Tonarink.Windowing;

static class AppWindows
{
    public static ReactorWindow OpenMain(bool startHidden)
    {
        var window = ReactorApp.OpenWindow(
            MainSpec(startHidden),
            () => new AppShell());
        if (startHidden)
            window.Hide();
        return window;
    }

    public static WindowSpec MainSpec(bool startHidden) => new()
    {
        Title = AppPlatform.DisplayName,
        Width = AppLayout.WindowWidth,
        Height = AppLayout.WindowHeight,
        MinWidth = AppLayout.WindowMinWidth,
        MinHeight = AppLayout.WindowMinHeight,
        Icon = AppPlatform.AppWindowIcon,
        // Keep the custom ShellTitleBar active when tray actions update the window spec.
        ExtendsContentIntoTitleBar = true,
        ShowInTaskbar = !startHidden,
    };
}
