using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using XamlToggleButton = Microsoft.UI.Xaml.Controls.Primitives.ToggleButton;

namespace Tonarink.Components.Animations;

sealed record AnimatedMultipleReceiversToggleProps(
    bool IsChecked,
    Action<bool> OnChanged,
    string AutomationName,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedMultipleReceiversToggle : Component<AnimatedMultipleReceiversToggleProps>
{
    public override Element Render()
    {
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var sourceRef = UseRef<Tonarink.MultipleReceiversIcon?>();
        var buttonRef = UseRef<XamlToggleButton?>();
        var previousCheckedRef = UseRef(Props.IsChecked);
        var reduceMotion = UseReducedMotion();

        UseEffect(() =>
        {
            var wasChecked = previousCheckedRef.Current;
            previousCheckedRef.Current = Props.IsChecked;
            if (wasChecked == Props.IsChecked)
                return;

            if (buttonRef.Current is { } button
                && sourceRef.Current is { } source)
            {
                button.DispatcherQueue.TryEnqueue(() =>
                    UpdateForeground(button, source, Props.IsChecked));
            }

            if (playerRef.Current is not { } player)
                return;

            player.Stop();
            if (reduceMotion)
            {
                player.SetProgress(Props.IsChecked ? 0.5 : 1);
                return;
            }

            if (Props.IsChecked)
            {
                player.SetProgress(0);
                _ = player.PlayAsync(fromProgress: 0, toProgress: 0.5, looped: false);
            }
            else
            {
                player.SetProgress(0.5);
                _ = player.PlayAsync(fromProgress: 0.5, toProgress: 1, looped: false);
            }
        }, Props.IsChecked, reduceMotion);

        var toggle = ToggleButton(string.Empty, Props.IsChecked, Props.OnChanged)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .MinWidth(40)
            .MinHeight(40)
            .IsEnabled(Props.IsEnabled)
            .OnMountAdd(element =>
            {
                if (element is not XamlToggleButton button)
                    return;

                buttonRef.Current = button;
                button.DispatcherQueue.TryEnqueue(() =>
                {
                    if (sourceRef.Current is { } source)
                        UpdateForeground(button, source, Props.IsChecked);
                });
            })
            .OnUnmountAdd(_ => buttonRef.Current = null);
        var icon = (AnimatedVisualPlayer() with { AutoPlay = false })
            .Size(24, 24)
            .Opacity(Props.IsEnabled ? 1 : 0.36)
            .IsHitTestVisible(false)
            .AccessibilityHidden()
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .OnMountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                var source = new Tonarink.MultipleReceiversIcon();
                UpdateForeground(buttonRef.Current, player, source, Props.IsChecked);
                player.Loaded += (_, _) =>
                {
                    UpdateForeground(buttonRef.Current, player, source, Props.IsChecked);
                    player.SetProgress(Props.IsChecked ? 0.5 : 0);
                };
                player.ActualThemeChanged += (_, _) =>
                    UpdateForeground(buttonRef.Current, player, source, Props.IsChecked);
                player.Source = source;
                player.SetProgress(Props.IsChecked ? 0.5 : 0);
                sourceRef.Current = source;
                playerRef.Current = player;
            })
            .OnUnmountAdd(element =>
            {
                if (element is AnimatedVisualPlayer player)
                    player.Stop();

                sourceRef.Current = null;
                playerRef.Current = null;
            });

        return Grid(
            columns: [GridSize.Auto],
            rows: [GridSize.Auto],
            toggle,
            icon);
    }

    private static void UpdateForeground(
        XamlToggleButton button,
        Tonarink.MultipleReceiversIcon source,
        bool isChecked)
    {
        if (isChecked)
        {
            source.Foreground = Microsoft.UI.Colors.Black;
            return;
        }

        if (button.Foreground is SolidColorBrush brush)
            source.Foreground = brush.Color;
    }

    private static void UpdateForeground(
        XamlToggleButton? button,
        AnimatedVisualPlayer player,
        Tonarink.MultipleReceiversIcon source,
        bool isChecked)
    {
        if (isChecked)
        {
            source.Foreground = Microsoft.UI.Colors.Black;
            return;
        }

        if (button?.Foreground is SolidColorBrush brush)
        {
            source.Foreground = brush.Color;
            return;
        }

        var theme = player.ActualTheme;
        if (theme == ElementTheme.Default
            && player.XamlRoot?.Content is FrameworkElement root)
        {
            theme = root.ActualTheme;
        }

        source.Foreground = theme == ElementTheme.Dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black;
    }
}
