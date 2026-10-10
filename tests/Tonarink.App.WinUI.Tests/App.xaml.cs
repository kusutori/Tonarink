using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Tonarink.App.WinUI.Tests;

public partial class TestApplication : Microsoft.UI.Xaml.Application
{
    internal static Window TestWindow { get; private set; } = null!;

    public TestApplication() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        TestWindow = new Window { Content = new Grid(), Title = "Tonarink WinUI tests" };
        TestWindow.Activate();
        UITestMethodAttribute.DispatcherQueue = TestWindow.DispatcherQueue;
        try
        {
            Environment.ExitCode = await MicrosoftTestingPlatformApplication.RunAsync(Environment.GetCommandLineArgs()[1..]);
        }
        finally
        {
            TestWindow.Close();
            Exit();
        }
    }
}
