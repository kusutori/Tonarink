namespace Tonarink.Hybrid;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App(MauiNodeLifecycle nodeLifecycle)
    {
        InitializeComponent();
        _ = nodeLifecycle.StartAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "Tonarink" };
    }
}
