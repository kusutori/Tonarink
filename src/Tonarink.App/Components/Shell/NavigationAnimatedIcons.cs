using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;

namespace Tonarink.Components.Shell;

static class NavigationAnimatedIcons
{
    public static void Attach(NavigationView navigationView)
    {
        SetIcon(navigationView, index: 0, new NavigationReceiveIcon(), AppIcons.Receive);
        SetIcon(navigationView, index: 1, new NavigationSendIcon(), AppIcons.Send);
        SetIcon(navigationView, index: 2, new AnimatedSettingsVisualSource(), AppIcons.Settings);
    }

    private static void SetIcon(
        NavigationView navigationView,
        int index,
        IAnimatedVisualSource2 source,
        string fallbackGlyph)
    {
        if (index >= navigationView.MenuItems.Count ||
            navigationView.MenuItems[index] is not NavigationViewItem item)
            return;

        var icon = new AnimatedIcon
        {
            Source = source,
            FallbackIconSource = new FontIconSource
            {
                Glyph = fallbackGlyph,
            },
        };

        // NavigationViewItem drives all subsequent pointer, press, and selection states.
        // Supplying the initial state before the icon enters the visual tree keeps the first
        // transition animated instead of making AnimatedIcon hard-cut to its destination.
        // AnimatedIcon.State inherits from the NavigationViewItem. Setting it on the
        // icon itself would create a local value and block the item's visual states,
        // leaving the Lottie source permanently parked on its Normal marker.
        item.Resources["NavigationViewItemOnLeftIconBoxHeight"] = 20d;
        AutomationProperties.SetAutomationId(item, index switch
        {
            0 => "Navigation.Receive",
            1 => "Navigation.Send",
            2 => "Navigation.Settings",
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        });
        AnimatedIcon.SetState(item, "Normal");
        item.Icon = icon;
    }
}
