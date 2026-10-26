using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Tonarink.Controls;

/// <summary>Keeps children mounted while their layout changes between a row and a column.</summary>
public sealed class AnimatedReflowPanel : Panel
{
    private readonly Dictionary<UIElement, ChildMotion> _motions = [];
    private readonly Dictionary<UIElement, Rect> _slots = [];
    private Orientation? _arrangedOrientation;
    private Orientation _orientation = Orientation.Horizontal;
    private double _spacing;
    private bool _animationsEnabled = true;

    public AnimatedReflowPanel() => Unloaded += (_, _) =>
    {
        ClearMotion();
        _slots.Clear();
        _arrangedOrientation = null;
    };

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value) return;
            _orientation = value;
            InvalidateMeasure();
        }
    }

    public double Spacing
    {
        get => _spacing;
        set
        {
            var spacing = double.IsFinite(value) ? Math.Max(0, value) : 0;
            if (_spacing.Equals(spacing)) return;
            _spacing = spacing;
            InvalidateMeasure();
        }
    }

    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set
        {
            if (_animationsEnabled == value) return;
            _animationsEnabled = value;
            if (!value) ClearMotion();
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;
        var horizontal = Orientation == Orientation.Horizontal;
        var gaps = Spacing * (Children.Count - 1);
        var width = horizontal && double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width - gaps) / Children.Count
            : availableSize.Width;
        var desiredWidth = 0d;
        var desiredHeight = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, horizontal ? availableSize.Height : double.PositiveInfinity));
            desiredWidth = horizontal ? desiredWidth + child.DesiredSize.Width : Math.Max(desiredWidth, child.DesiredSize.Width);
            desiredHeight = horizontal ? Math.Max(desiredHeight, child.DesiredSize.Height) : desiredHeight + child.DesiredSize.Height;
        }
        return new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : desiredWidth + (horizontal ? gaps : 0),
            desiredHeight + (horizontal ? 0 : gaps));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var stale in _slots.Keys.Where(child => !Children.Contains(child)).ToArray())
        {
            if (_motions.Remove(stale, out var motion)) motion.Dispose();
            _slots.Remove(stale);
        }

        var horizontal = Orientation == Orientation.Horizontal;
        var reflow = AnimationsEnabled && _arrangedOrientation is { } previousOrientation && previousOrientation != Orientation;
        var width = horizontal && Children.Count > 0
            ? Math.Max(0, finalSize.Width - Spacing * (Children.Count - 1)) / Children.Count
            : finalSize.Width;
        var offset = 0d;
        foreach (var child in Children)
        {
            var height = horizontal ? finalSize.Height : child.DesiredSize.Height;
            var bounds = horizontal
                ? new Rect(offset, 0, width, height)
                : new Rect(0, offset, width, height);
            child.Arrange(bounds);
            offset += (horizontal ? width : height) + Spacing;

            // Only a row/column switch animates. Normal window resizing follows layout
            // directly instead of continually restarting an implicit Offset animation.
            if (reflow && _slots.TryGetValue(child, out var previous))
            {
                var displacement = new Vector3((float)(previous.X - bounds.X), (float)(previous.Y - bounds.Y), 0);
                if (displacement != Vector3.Zero)
                {
                    if (!_motions.TryGetValue(child, out var motion))
                        _motions.Add(child, motion = new ChildMotion(child));
                    motion.Start(displacement);
                }
            }
            _slots[child] = bounds;
        }
        _arrangedOrientation = Orientation;
        return finalSize;
    }

    private void ClearMotion()
    {
        foreach (var motion in _motions.Values) motion.Dispose();
        _motions.Clear();
    }

    private sealed class ChildMotion : IDisposable
    {
        private readonly UIElement _child;
        private readonly Vector3 _restingTranslation;
        private readonly Vector3KeyFrameAnimation _offset;

        public ChildMotion(UIElement child)
        {
            _child = child;
            _restingTranslation = child.Translation;
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            _offset = compositor.CreateVector3KeyFrameAnimation();
            _offset.Target = "Translation";
            _offset.Duration = TimeSpan.FromMilliseconds(220);
            using var easing = compositor.CreateCubicBezierEasingFunction(new(0.2f, 0.8f), new(0.2f, 1));
            _offset.InsertExpressionKeyFrame(0, "this.StartingValue + displacement");
            _offset.InsertKeyFrame(1, _restingTranslation, easing);
        }

        public void Start(Vector3 displacement)
        {
            // Translation is independent of layout's Offset. A resize during the
            // animation can update the real slot without fighting the interpolation.
            _offset.SetVector3Parameter("displacement", displacement);
            _child.StartAnimation(_offset);
        }

        public void Dispose()
        {
            _child.StopAnimation(_offset);
            _child.Translation = _restingTranslation;
            _offset.Dispose();
        }
    }
}
