using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

enum UndoRedoIconKind
{
    Undo,
    Redo,
}

sealed record AnimatedUndoRedoButtonProps(
    UndoRedoIconKind Kind,
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true,
    double IconSize = 24);

sealed class AnimatedUndoRedoButton : Component<AnimatedUndoRedoButtonProps>
{
    public override Element Render()
    {
        var source = UseMemo(() => CreateSource(Props.Kind), Props.Kind);
        var fallback = UseMemo(() => new FontIconSource
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = Props.Kind switch
            {
                UndoRedoIconKind.Undo => AppIcons.Undo,
                UndoRedoIconKind.Redo => AppIcons.Redo,
            },
        }, Props.Kind);

        // The standard Button template drives AnimatedIcon.State through its
        // ContentPresenter. Inherit it rather than setting a local icon state:
        // press holds the compressed pose, release plays the spring-back.
        // WinUI also handles cancellation, keyboard input, foreground colors,
        // rapid transitions and the system's reduced-motion setting.
        return Button(
                AnimatedIcon(source, fallback)
                    .Size(Props.IconSize, Props.IconSize)
                    .IsHitTestVisible(false)
                    .AccessibilityHidden(),
                Props.OnClick)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(40, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled);
    }

    private static IAnimatedVisualSource2 CreateSource(UndoRedoIconKind kind) => kind switch
    {
        UndoRedoIconKind.Undo => new Tonarink.UndoFlowIcon(),
        UndoRedoIconKind.Redo => new Tonarink.RedoFlowIcon(),
    };
}
