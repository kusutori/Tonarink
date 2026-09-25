using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record SegmentedContentSwitcherProps(
    int SelectedIndex,
    Element First,
    Element Second);

/// <summary>
/// Keeps both Segmented pages mounted and animates selection changes horizontally.
/// The selector remains a separate control so this host can be reused with any tab header.
/// </summary>
sealed class SegmentedContentSwitcher : Component<SegmentedContentSwitcherProps>
{
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan EnterDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(280);

    public override Element Render()
    {
        var selectedIndex = Math.Clamp(Props.SelectedIndex, 0, 1);
        var previousIndex = UseRef(selectedIndex);
        var generation = UseRef(0);
        var firstRef = UseRef<FrameworkElement?>();
        var secondRef = UseRef<FrameworkElement?>();
        var alive = UseRef(true);

        UseEffect(() =>
        {
            alive.Current = true;
            return () =>
            {
                alive.Current = false;
                generation.Current++;
            };
        });

        UseEffect(() =>
        {
            var outgoingIndex = previousIndex.Current;
            if (outgoingIndex == selectedIndex)
                return;

            previousIndex.Current = selectedIndex;
            var outgoing = outgoingIndex == 0 ? firstRef.Current : secondRef.Current;
            var incoming = selectedIndex == 0 ? firstRef.Current : secondRef.Current;
            if (outgoing is null || incoming is null)
                return;

            Animate(outgoing, incoming, selectedIndex > outgoingIndex ? 1 : -1, ++generation.Current);
        }, selectedIndex);

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                Page(Props.First, 0, firstRef).Grid(row: 0, column: 0),
                Page(Props.Second, 1, secondRef).Grid(row: 0, column: 0))
            .OnMountAdd(element =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                visual.Clip = visual.Compositor.CreateInsetClip();
            })
            .OnUnmountAdd(element =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                visual.Clip = null;
            });

        Element Page(Element content, int index, Ref<FrameworkElement?> elementRef) =>
            Border(content)
                .WithKey($"segmented-page-{index}")
                .IsHitTestVisible(index == selectedIndex)
                .OnMountAdd(element =>
                {
                    elementRef.Current = element;
                    ResetPage(element, visible: index == selectedIndex);
                })
                .OnUnmountAdd(element =>
                {
                    if (ReferenceEquals(elementRef.Current, element))
                        elementRef.Current = null;
                    StopAnimations(element);
                });

        void Animate(FrameworkElement outgoing, FrameworkElement incoming, int direction, int animationGeneration)
        {
            if (!AnimationsEnabled())
            {
                ResetPage(outgoing, visible: false);
                ResetPage(incoming, visible: true);
                return;
            }

            outgoing.Visibility = Visibility.Visible;
            incoming.Visibility = Visibility.Visible;

            var outgoingVisual = ElementCompositionPreview.GetElementVisual(outgoing);
            var incomingVisual = ElementCompositionPreview.GetElementVisual(incoming);
            StopAnimations(outgoingVisual);
            StopAnimations(incomingVisual);

            var compositor = incomingVisual.Compositor;
            var exitEasing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.7f, 0),
                new Vector2(1, 0.5f));
            var enterEasing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.1f, 0.9f),
                new Vector2(0.2f, 1));

            outgoingVisual.Offset = Vector3.Zero;
            outgoingVisual.Opacity = 1;
            incomingVisual.Offset = new Vector3(200 * direction, 0, 0);
            incomingVisual.Opacity = 0;

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            StartVectorAnimation(
                outgoingVisual,
                new Vector3(-150 * direction, 0, 0),
                ExitDuration,
                TimeSpan.Zero,
                exitEasing);
            StartOpacityAnimation(outgoingVisual, 0, ExitDuration, TimeSpan.Zero, exitEasing);
            StartVectorAnimation(
                incomingVisual,
                Vector3.Zero,
                EnterDuration,
                EnterDelay,
                enterEasing);
            StartOpacityAnimation(incomingVisual, 1, EnterDuration, EnterDelay, enterEasing);
            batch.Completed += (_, _) =>
            {
                try
                {
                    if (!alive.Current || generation.Current != animationGeneration)
                        return;

                    ResetPage(outgoing, visible: false);
                    ResetPage(incoming, visible: true);
                }
                finally
                {
                    batch.Dispose();
                }
            };
            batch.End();
        }
    }

    private static void StartVectorAnimation(
        Visual visual,
        Vector3 target,
        TimeSpan duration,
        TimeSpan delay,
        CompositionEasingFunction easing)
    {
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1, target, easing);
        animation.Duration = duration;
        animation.DelayTime = delay;
        visual.StartAnimation(nameof(visual.Offset), animation);
    }

    private static void StartOpacityAnimation(
        Visual visual,
        float target,
        TimeSpan duration,
        TimeSpan delay,
        CompositionEasingFunction easing)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, target, easing);
        animation.Duration = duration;
        animation.DelayTime = delay;
        visual.StartAnimation(nameof(visual.Opacity), animation);
    }

    private static void ResetPage(FrameworkElement element, bool visible)
    {
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        StopAnimations(visual);
        visual.Offset = Vector3.Zero;
        visual.Opacity = visible ? 1 : 0;
    }

    private static void StopAnimations(FrameworkElement element) =>
        StopAnimations(ElementCompositionPreview.GetElementVisual(element));

    private static void StopAnimations(Visual visual)
    {
        visual.StopAnimation(nameof(visual.Offset));
        visual.StopAnimation(nameof(visual.Opacity));
    }

    private static bool AnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Report("Could not read the Windows animation preference", exception);
            return false;
        }
    }
}
