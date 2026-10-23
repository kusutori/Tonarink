using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;

namespace Tonarink.Components.Animations;

sealed record AnimatedShuffleSplitButtonProps(string AutomationName, Action OnClick, Element Flyout);

sealed class AnimatedShuffleSplitButton : Component<AnimatedShuffleSplitButtonProps>
{
    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var alternate = UseRef(false);
        // SplitButton currently exposes only a string Label, not an Element
        // content slot. Keep one native AnimatedIcon for this unwrapped slot.
        var icon = UseMemo(() => new XamlAnimatedIcon
        {
            Source = new ShufflePinIcon(),
            FallbackIconSource = new FontIconSource
            {
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                Glyph = AppIcons.Shuffle,
            },
            Width = 20,
            Height = 20,
            IsHitTestVisible = false,
        });

        return SplitButton("", () =>
            {
                if (!reduceMotion)
                {
                    alternate.Current = !alternate.Current;
                    XamlAnimatedIcon.SetState(icon, alternate.Current ? "Shuffled" : "Normal");
                }

                Props.OnClick();
            }, Props.Flyout)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.AutomationName)
            .Set(button =>
            {
                if (!ReferenceEquals(button.Content, icon))
                    button.Content = icon;
            });
    }
}
