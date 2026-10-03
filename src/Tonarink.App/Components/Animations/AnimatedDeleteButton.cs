using System.Numerics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;
using XamlButton = Microsoft.UI.Xaml.Controls.Button;

namespace Tonarink.Components.Animations;

sealed record AnimatedDeleteButtonProps(
    string AutomationName,
    Action OnClick,
    string? Label = null,
    string? ToolTip = null,
    bool IsEnabled = true,
    bool Subtle = false,
    bool Critical = false,
    int ShakeVersion = 0,
    double IconSize = 24);

sealed class AnimatedDeleteButton : Component<AnimatedDeleteButtonProps>
{
    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var playbackRef = UseRef<DeleteButtonPlayback?>();
        var playback = playbackRef.Current ??= new DeleteButtonPlayback();
        var previousShakeVersionRef = UseRef(Props.ShakeVersion);
        playback.UseCriticalForeground = Props.Critical && Props.IsEnabled;

        UseEffect(() =>
        {
            UpdateForeground(playback);
        }, Props.Critical, Props.IsEnabled);

        UseEffect(() =>
        {
            var shouldShake = Props.ShakeVersion > previousShakeVersionRef.Current;
            previousShakeVersionRef.Current = Props.ShakeVersion;
            if (!shouldShake)
                return;

            CloseImmediately(playback);
            if (!reduceMotion)
                PlayShake(playback);
        }, Props.ShakeVersion, reduceMotion);

        var player = DeleteIcon(playback, Props.IsEnabled, Props.IconSize);
        Element content = Props.Label is null
            ? player
            : HStack(8, player, TextBlock(Props.Label));

        var button = Button(content, Props.OnClick)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .IsEnabled(Props.IsEnabled)
            .OnMountAdd(element => AttachPointerHandlers(element, playback, reduceMotion))
            .OnUnmountAdd(element => DetachPointerHandlers(element, playback));

        if (Props.Label is null)
        {
            button = button
                .MinWidth(40)
                .MinHeight(40);
        }

        if (Props.Subtle)
            button = button.SubtleButton();

        if (Props.Critical)
        {
            button = button.Resources(static resources => resources
                .Set("ButtonForeground", Theme.SystemCritical)
                .Set("ButtonForegroundPointerOver", Theme.SystemCritical)
                .Set("ButtonForegroundPressed", Theme.SystemCritical)
                .Set("ButtonForegroundDisabled", Theme.DisabledText));
        }

        return button;
    }

    private static Element DeleteIcon(
        DeleteButtonPlayback playback,
        bool isEnabled,
        double iconSize) =>
        (AnimatedVisualPlayer() with { AutoPlay = false })
        .Size(iconSize, iconSize)
        .Opacity(isEnabled ? 1 : 0.36)
        .IsHitTestVisible(false)
        .AccessibilityHidden()
        .OnMountAdd(element =>
        {
            if (element is not AnimatedVisualPlayer player)
                return;

            var source = new Tonarink.DeleteIcon();
            playback.Player = player;
            playback.Source = source;
            UpdateForeground(playback);
            player.Loaded += (_, _) => UpdateForeground(playback);
            player.ActualThemeChanged += (_, _) => UpdateForeground(playback);
            player.Source = source;
            player.SetProgress(0);
            BindCompositionCenterPoint(player);
        })
        .OnUnmountAdd(element =>
        {
            if (element is AnimatedVisualPlayer player)
                player.Stop();

            playback.Player = null;
            playback.Source = null;
        });

    private static void AttachPointerHandlers(
        UIElement element,
        DeleteButtonPlayback playback,
        bool reduceMotion)
    {
        if (element is not XamlButton button)
            return;

        PointerEventHandler entered = (_, _) =>
        {
            if (!reduceMotion)
                PlayHover(playback);
        };
        PointerEventHandler exited = (_, _) =>
        {
            CloseImmediately(playback);
        };

        button.AddHandler(UIElement.PointerEnteredEvent, entered, handledEventsToo: true);
        button.AddHandler(UIElement.PointerExitedEvent, exited, handledEventsToo: true);
        playback.DetachPointerHandlers = () =>
        {
            button.RemoveHandler(UIElement.PointerEnteredEvent, entered);
            button.RemoveHandler(UIElement.PointerExitedEvent, exited);
        };
    }

    private static void DetachPointerHandlers(
        UIElement element,
        DeleteButtonPlayback playback)
    {
        playback.DetachPointerHandlers?.Invoke();
        playback.DetachPointerHandlers = null;
        if (playback.Player is { } player)
        {
            player.Stop();
            ResetShake(player);
        }

        playback.Player = null;
    }

    private static void PlayHover(DeleteButtonPlayback playback)
    {
        if (playback.Player is not { } player)
            return;

        player.Stop();
        _ = player.PlayAsync(
            fromProgress: 0,
            toProgress: 1,
            looped: false);
    }

    private static void CloseImmediately(DeleteButtonPlayback playback)
    {
        if (playback.Player is not { } player)
            return;

        player.Stop();
        player.SetProgress(0);
    }

    private static void PlayShake(DeleteButtonPlayback playback)
    {
        if (playback.Player is not { } player)
            return;

        var visual = ElementCompositionPreview.GetElementVisual(player);
        visual.StopAnimation(nameof(visual.RotationAngleInDegrees));
        visual.RotationAngleInDegrees = 0;

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0);
        animation.InsertKeyFrame(0.16f, -7);
        animation.InsertKeyFrame(0.34f, 6);
        animation.InsertKeyFrame(0.52f, -4);
        animation.InsertKeyFrame(0.70f, 3);
        animation.InsertKeyFrame(0.86f, -1.5f);
        animation.InsertKeyFrame(1, 0);
        animation.Duration = TimeSpan.FromMilliseconds(340);
        visual.StartAnimation(nameof(visual.RotationAngleInDegrees), animation);
    }

    private static void BindCompositionCenterPoint(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var centerPoint = visual.Compositor.CreateExpressionAnimation(
            "Vector3(target.Size.X / 2, target.Size.Y * 0.88, 0)");
        centerPoint.SetReferenceParameter("target", visual);
        visual.StartAnimation(nameof(visual.CenterPoint), centerPoint);
    }

    private static void ResetShake(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation(nameof(visual.RotationAngleInDegrees));
        visual.RotationAngleInDegrees = 0;
        visual.StopAnimation(nameof(visual.CenterPoint));
        visual.CenterPoint = Vector3.Zero;
    }

    private static void UpdateForeground(
        AnimatedVisualPlayer player,
        Tonarink.DeleteIcon source,
        bool critical)
    {
        if (critical
            && Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(
                "SystemFillColorCritical",
                out var resource)
            && resource is Windows.UI.Color criticalColor)
        {
            source.Foreground = criticalColor;
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

    private static void UpdateForeground(DeleteButtonPlayback playback)
    {
        if (playback.Player is { } player && playback.Source is { } source)
            UpdateForeground(player, source, playback.UseCriticalForeground);
    }

    private sealed class DeleteButtonPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public Tonarink.DeleteIcon? Source { get; set; }

        public bool UseCriticalForeground { get; set; }

        public Action? DetachPointerHandlers { get; set; }
    }
}
