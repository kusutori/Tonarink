using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace Tonarink.Pages.History;

// Layout stays native. Only clips and the More button's translation animate.
sealed class HistoryTypeReveal
{
    public const double ItemGap = 8;
    private const int MotionSamples = 60;
    private readonly Dictionary<FileTypeGlyphKind, FrameworkElement> _items = [];
    private readonly List<RevealedItem> _revealed = [];
    private FrameworkElement? _root;
    private FrameworkElement? _more;
    private CompositionScopedBatch? _batch;
    private Vector3KeyFrameAnimation? _movement;
    private Vector3 _restingTranslation;
    private Vector3 _start;
    private Size _layoutSize;
    private Action? _completed;
    private bool _collapsing;
    private bool _awaitingCollapseLayout;
    private int _generation;

    public void AttachRoot(FrameworkElement root)
    {
        _root = root;
        root.Unloaded += OnUnloaded;
    }

    public void DetachRoot(FrameworkElement root)
    {
        if (!ReferenceEquals(_root, root)) return;
        Complete(notify: false);
        root.Unloaded -= OnUnloaded;
        _root = null;
        _items.Clear();
    }

    public void AttachMore(FrameworkElement more) => _more = more;

    public void DetachMore(FrameworkElement more)
    {
        if (!ReferenceEquals(_more, more)) return;
        Complete(notify: false);
        _more = null;
    }

    public void AttachItem(FileTypeGlyphKind kind, FrameworkElement element)
    {
        _items[kind] = element;
        if (_completed is null || _collapsing) return;
        var item = new RevealedItem(element);
        _revealed.Add(item);
        item.Visual.Opacity = 0;
        item.SuspendInput();
    }

    public void DetachItem(FileTypeGlyphKind kind, FrameworkElement element)
    {
        if (_items.TryGetValue(kind, out var current) && ReferenceEquals(current, element))
            _items.Remove(kind);
        var item = _revealed.Find(candidate => ReferenceEquals(candidate.Element, element));
        if (item is null) return;
        item.Restore();
        _revealed.Remove(item);
    }

    public bool Begin(Action completed)
    {
        if (_root is not { IsLoaded: true } root || _more is not { } more
            || _completed is not null || _awaitingCollapseLayout)
            return false;

        _collapsing = false;
        _start = Position(more, root);
        _restingTranslation = more.Translation;
        _completed = completed;
        root.LayoutUpdated += OnLayoutUpdated;
        root.SizeChanged += OnSizeChanged;
        return true;
    }

    public bool BeginCollapse(IReadOnlySet<FileTypeGlyphKind> retained, Action completed)
    {
        if (_root is not { IsLoaded: true } root || _more is not { } more
            || _completed is not null || _awaitingCollapseLayout)
            return false;
        var removed = _items.Where(item => !retained.Contains(item.Key)).Select(item => item.Value).ToArray();
        if (removed.Length == 0) return false;

        _collapsing = true;
        _restingTranslation = more.Translation;
        _completed = completed;
        var destination = CollapsedPosition(retained, root, more);
        foreach (var element in removed)
        {
            var item = new RevealedItem(element);
            item.SuspendInput();
            _revealed.Add(item);
        }
        root.SizeChanged += OnSizeChanged;
        StartMotion(root, more, destination, Position(more, root));
        return true;
    }

    public void Complete() => Complete(notify: true);

    private void OnUnloaded(object sender, RoutedEventArgs args) => Complete();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_batch is not null
            && (Math.Abs(args.NewSize.Width - _layoutSize.Width) > 0.5
                || Math.Abs(args.NewSize.Height - _layoutSize.Height) > 0.5))
            Complete();
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (_root is not { } root || _more is not { ActualWidth: > 0 } more) return;
        if (_awaitingCollapseLayout)
        {
            // Removed controls have unmounted and the native button has reached
            // its collapsed slot. Release the held animation endpoint here.
            if (_revealed.Count != 0) return;
            Complete(notify: false);
            return;
        }
        // Layout can tick before Reactor commits the expanded children.
        if (_completed is null || _revealed.Count == 0) return;
        if (_revealed.Any(item => item.Element.ActualWidth <= 0 || item.Element.ActualHeight <= 0)) return;
        root.LayoutUpdated -= OnLayoutUpdated;
        StartMotion(root, more, _start, Position(more, root));
    }

    private void StartMotion(FrameworkElement root, FrameworkElement more, Vector3 start, Vector3 end)
    {
        _layoutSize = new Size(root.ActualWidth, root.ActualHeight);
        var ordered = _revealed.OrderBy(item => Position(item.Element, root).Y)
            .ThenBy(item => Position(item.Element, root).X).ToArray();
        var path = new List<(Vector3 Position, float Distance)> { (start, 0) };
        var intervals = new List<(RevealedItem Item, float Start, float End)>();

        void AddPoint(Vector3 point)
        {
            var last = path[^1];
            path.Add((point, last.Distance + Vector3.Distance(last.Position, point)));
        }

        foreach (var item in ordered)
        {
            var left = Position(item.Element, root);
            AddPoint(left);
            var from = path[^1].Distance;
            AddPoint(left + new Vector3((float)item.Element.ActualWidth, 0, 0));
            intervals.Add((item, from, path[^1].Distance));
        }
        AddPoint(end);
        var distance = path[^1].Distance;
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(distance / 1.8, 380, 760));
        var compositor = ordered[0].Visual.Compositor;
        using var linear = compositor.CreateLinearEasingFunction();
        _movement = compositor.CreateVector3KeyFrameAnimation();
        _movement.Target = nameof(UIElement.Translation);
        _movement.Duration = duration;
        // One acceleration/deceleration envelope covers the WHOLE path. The
        // clips sample the same distance, so their edge stays with the button.
        for (var sample = 0; sample <= MotionSamples; sample++)
        {
            var time = (float)sample / MotionSamples;
            var traveled = distance * TravelProgress(time, _collapsing);
            _movement.InsertKeyFrame(time, _restingTranslation + PointAt(path, traveled) - end, linear);
        }

        _batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        foreach (var (item, from, to) in intervals)
        {
            var width = (float)item.Element.ActualWidth;
            var clip = compositor.CreateInsetClip();
            clip.RightInset = _collapsing ? 0 : width;
            item.Clip = clip;
            item.Visual.Clip = clip;
            using var wipe = compositor.CreateScalarKeyFrameAnimation();
            wipe.Duration = duration;
            for (var sample = 0; sample <= MotionSamples; sample++)
            {
                var time = (float)sample / MotionSamples;
                var traveled = distance * TravelProgress(time, _collapsing);
                var visible = Math.Clamp((traveled - from) / (to - from), 0, 1);
                wipe.InsertKeyFrame(time, width * (1 - visible), linear);
            }
            clip.StartAnimation(nameof(InsetClip.RightInset), wipe);
            item.Visual.Opacity = 1;
        }
        more.StartAnimation(_movement);
        _batch.Completed += OnCompleted;
        _batch.End();
    }

    private void OnCompleted(object sender, CompositionBatchCompletedEventArgs args)
    {
        var generation = _generation;
        _root?.DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _generation) return;
            if (!_collapsing)
            {
                Complete();
                return;
            }

            // Hold the final pose until the state change commits. Restoring the
            // clips/translation first would flash the expanded layout for a frame.
            var completed = _completed;
            _completed = null;
            _awaitingCollapseLayout = true;
            _root!.LayoutUpdated += OnLayoutUpdated;
            completed?.Invoke();
        });
    }

    private void Complete(bool notify)
    {
        _generation++;
        var completed = _completed;
        _completed = null;
        _awaitingCollapseLayout = false;
        _collapsing = false;
        if (_root is { } root)
        {
            root.LayoutUpdated -= OnLayoutUpdated;
            root.SizeChanged -= OnSizeChanged;
        }
        if (_batch is { } batch)
        {
            batch.Completed -= OnCompleted;
            batch.Dispose();
            _batch = null;
        }
        if (_more is { } more && _movement is { } movement)
        {
            more.StopAnimation(movement);
            more.Translation = _restingTranslation;
        }
        _movement?.Dispose();
        _movement = null;
        foreach (var item in _revealed) item.Restore();
        _revealed.Clear();
        if (notify) completed?.Invoke();
    }

    private Vector3 CollapsedPosition(IReadOnlySet<FileTypeGlyphKind> retained,
        FrameworkElement root, FrameworkElement more)
    {
        // Pack surviving intrinsic sizes using this picker's gap/wrap rule.
        // No live controls are detached or remeasured during the animation.
        double x = 0, y = 0, rowHeight = 0;
        var survivors = _items.Where(item => retained.Contains(item.Key)).Select(item => item.Value)
            .OrderBy(item => Position(item, root).Y).ThenBy(item => Position(item, root).X);
        foreach (var element in survivors.Append(more))
        {
            var width = element.DesiredSize.Width;
            if (x > 0 && x + width > root.ActualWidth + 0.5)
            {
                x = 0;
                y += rowHeight + ItemGap;
                rowHeight = 0;
            }
            if (ReferenceEquals(element, more)) return new Vector3((float)x, (float)y, 0);
            x += width + ItemGap;
            rowHeight = Math.Max(rowHeight, element.DesiredSize.Height);
        }
        return Vector3.Zero;
    }

    private static float TravelProgress(float time, bool reverse)
    {
        // Smootherstep has zero velocity AND acceleration at both endpoints.
        var progress = time * time * time * (time * (time * 6 - 15) + 10);
        return reverse ? 1 - progress : progress;
    }

    private static Vector3 PointAt(List<(Vector3 Position, float Distance)> path, float distance)
    {
        for (var index = 1; index < path.Count; index++)
        {
            var previous = path[index - 1];
            var next = path[index];
            var length = next.Distance - previous.Distance;
            if (distance <= next.Distance && length > 0)
                return Vector3.Lerp(previous.Position, next.Position,
                    Math.Clamp((distance - previous.Distance) / length, 0, 1));
        }
        return path[^1].Position;
    }

    private static Vector3 Position(FrameworkElement element, UIElement root)
    {
        var point = element.TransformToVisual(root).TransformPoint(new Point());
        return new Vector3((float)point.X, (float)point.Y, 0);
    }

    private sealed class RevealedItem(FrameworkElement element)
    {
        public FrameworkElement Element { get; } = element;
        public Visual Visual { get; } = ElementCompositionPreview.GetElementVisual(element);
        public InsetClip? Clip { get; set; }
        private CompositionClip? OriginalClip { get; } = ElementCompositionPreview.GetElementVisual(element).Clip;
        private bool HitTestVisible { get; } = element.IsHitTestVisible;
        private bool TabStop { get; } = element is Control { IsTabStop: true };

        public void SuspendInput()
        {
            Element.IsHitTestVisible = false;
            if (Element is Control control) control.IsTabStop = false;
        }

        public void Restore()
        {
            Visual.Opacity = 1;
            Visual.Clip = OriginalClip;
            Clip?.StopAnimation(nameof(InsetClip.RightInset));
            Clip?.Dispose();
            Clip = null;
            Element.IsHitTestVisible = HitTestVisible;
            if (Element is Control control) control.IsTabStop = TabStop;
        }
    }
}
