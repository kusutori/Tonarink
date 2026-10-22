using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Tonarink.Components.Animations;

static class AnimatedFeedback
{
    public static Element Outgoing(Element icon, string key, int version, bool playing) =>
        icon.Keyframes(key, version, keyframes => playing
                ? keyframes
                    .Duration(1433)
                    .At(0.000f, opacity: 1, scale: new(1, 1, 1))
                    .At(0.093f, opacity: 0, scale: new(0.273f, 0.273f, 1),
                        easing: Easing.CubicBezier(0.13f, 0, 0, 1))
                    .At(0.814f, opacity: 0, scale: new(0.273f, 0.273f, 1))
                    .At(0.837f, opacity: 0, scale: new(1, 1, 1))
                    .At(0.907f, opacity: 0, scale: new(1, 1, 1))
                    .At(1.000f, opacity: 1, scale: new(1, 1, 1), easing: Easing.EaseOut)
                : keyframes
                    .Duration(1)
                    .At(0f, opacity: 1, scale: new(1, 1, 1))
                    .At(1f, opacity: 1, scale: new(1, 1, 1)));

    public static Element Incoming(Element icon, string key, int version, bool playing) =>
        icon.Opacity(0).Keyframes(key, version, keyframes => playing
                ? keyframes
                    .Duration(1433)
                    .At(0.000f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                    .At(0.093f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                    .At(0.186f, opacity: 1, scale: new(1.146f, 1.146f, 1),
                        easing: Easing.CubicBezier(0.39f, 0, 0.63f, 1))
                    .At(0.232f, opacity: 1, scale: new(1, 1, 1),
                        easing: Easing.CubicBezier(0.55f, 0, 0.02f, 1))
                    .At(0.814f, opacity: 1, scale: new(1, 1, 1))
                    .At(0.907f, opacity: 0, scale: new(0.385f, 0.385f, 1),
                        easing: Easing.EaseIn)
                    .At(1.000f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                : keyframes
                    .Duration(1)
                    .At(0f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                    .At(1f, opacity: 0, scale: new(0.385f, 0.385f, 1)));

    public static void BindCompositionCenterPoint(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var centerPoint = visual.Compositor.CreateExpressionAnimation(
            "Vector3(target.Size.X / 2, target.Size.Y / 2, 0)");
        centerPoint.SetReferenceParameter("target", visual);
        visual.StartAnimation("CenterPoint", centerPoint);
    }
}
