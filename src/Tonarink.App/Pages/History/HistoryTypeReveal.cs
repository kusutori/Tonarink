using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace Tonarink.Pages.History;

// Layout stays native. Only the reveal clips and the More button's translation
// animate, so the type buttons retain their native checked state and focus.
sealed class HistoryTypeReveal
{
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
        if (_completed is null) return;

        // Newly mounted items reserve their final layout slots, but cannot be
        // seen or focused until the moving button passes over them.
        var item = new RevealedItem(element);
        _revealed.Add(item);
        item.Visual.Opacity = 0;
        element.IsHitTestVisible = false;
        if (element is Control control) control.IsTabStop = false;
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
        if (_root is not { IsLoaded: true } root || _more is not { } more || _completed is not null)
            return false;

        _start = Position(more, root);
        _restingTranslation = more.Translation;
        _completed = completed;
        root.LayoutUpdated += OnLayoutUpdated;
        root.SizeChanged += OnSizeChanged;
        return true;
    }

    public void Complete() => Complete(notify: true);

    private void OnUnloaded(object sender, RoutedEventArgs args) => Complete();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // A resize invalidates the measured sweep path. Settle immediately
        // instead of chasing moving targets or restarting on every layout.
        if (_batch is not null
            && (Math.Abs(args.NewSize.Width - _layoutSize.Width) > 0.5
                || Math.Abs(args.NewSize.Height - _layoutSize.Height) > 0.5))
            Complete();
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (_root is not { } root || _more is not { ActualWidth: > 0 } more || _completed is null)
            return;
        // Layout can tick before Reactor commits the expanded tree. Wait for
        // the new children instead of completing against the collapsed layout.
        if (_revealed.Count == 0) return;
        if (_revealed.Any(item => item.Element.ActualWidth <= 0 || item.Element.ActualHeight <= 0))
            return;

        root.LayoutUpdated -= OnLayoutUpdated;
        _layoutSize = new Size(root.ActualWidth, root.ActualHeight);
        var destination = Position(more, root);
        var ordered = _revealed.OrderBy(item => Position(item.Element, root).Y)
            .ThenBy(item => Position(item.Element, root).X).ToArray();
        var segments = new List<(Vector3 Position, double Time)> { (_start, 0) };
        var intervals = new List<(RevealedItem Item, double Start, double End)>();
        var current = _start;
        var elapsed = 0d;

        foreach (var item in ordered)
        {
            var left = Position(item.Element, root);
            elapsed += Math.Clamp(Vector3.Distance(current, left) / 2.5, 20, 80);
            segments.Add((left, elapsed));
            var start = elapsed;
            elapsed += Math.Clamp(item.Element.ActualWidth / 1.5, 60, 120);
            current = left + new Vector3((float)item.Element.ActualWidth, 0, 0);
            segments.Add((current, elapsed));
            intervals.Add((item, start, elapsed));
        }
        elapsed += 80;
        segments.Add((destination, elapsed));
        var duration = TimeSpan.FromMilliseconds(elapsed);
        var compositor = ordered[0].Visual.Compositor;
        using var linear = compositor.CreateLinearEasingFunction();
        _movement = compositor.CreateVector3KeyFrameAnimation();
        _movement.Target = nameof(UIElement.Translation);
        _movement.Duration = duration;
        foreach (var segment in segments)
            _movement.InsertKeyFrame((float)(segment.Time / elapsed),
                _restingTranslation + segment.Position - destination, linear);

        _batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        foreach (var (item, start, end) in intervals)
        {
            var clip = compositor.CreateInsetClip();
            clip.RightInset = (float)item.Element.ActualWidth;
            item.Clip = clip;
            item.Visual.Clip = clip;
            using var reveal = compositor.CreateScalarKeyFrameAnimation();
            reveal.InsertKeyFrame(0, clip.RightInset);
            reveal.InsertKeyFrame((float)(start / elapsed), clip.RightInset, linear);
            reveal.InsertKeyFrame((float)(end / elapsed), 0, linear);
            reveal.InsertKeyFrame(1, 0, linear);
            // Keep the clips in the batch for the whole sweep, including the
            // button's final move. The button now remains visible for the next batch.
            reveal.Duration = duration;
            clip.StartAnimation(nameof(InsetClip.RightInset), reveal);
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
            if (generation == _generation) Complete();
        });
    }

    private void Complete(bool notify)
    {
        _generation++;
        var completed = _completed;
        _completed = null;
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
