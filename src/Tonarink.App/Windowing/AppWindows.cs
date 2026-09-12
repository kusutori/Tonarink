using Microsoft.UI.Reactor;

namespace Tonarink.Windowing;

static class AppWindows
{
    public static ReactorWindow OpenMain(bool startHidden)
    {
        var window = ReactorApp.OpenWindow(
            new WindowSpec
            {
                Title = "Tonarink",
                Width = AppLayout.WindowWidth,
                Height = AppLayout.WindowHeight,
                MinWidth = AppLayout.WindowMinWidth,
                MinHeight = AppLayout.WindowMinHeight,
                Icon = AppPlatform.AppWindowIcon,
                ShowInTaskbar = !startHidden,
            },
            () => new AppShell());
        if (startHidden)
            window.Hide();
        return window;
    }
}
