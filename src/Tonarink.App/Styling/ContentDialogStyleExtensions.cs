using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Tonarink.Styling;

static class ContentDialogStyleExtensions
{
    /// <summary>Applies the app theme to a dialog hosted outside the page visual tree.</summary>
    public static ContentDialogElement Themed(this ContentDialogElement dialog, ElementTheme theme) =>
        dialog.Set(control => control.RequestedTheme = theme);
}
