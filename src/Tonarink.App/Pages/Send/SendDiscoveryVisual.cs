using System.Numerics;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages.Send;

sealed record SendDiscoveryVisualProps(LocalSendNodeState NodeState);

sealed class SendDiscoveryVisual : Component<SendDiscoveryVisualProps>
{
    private const double Size = 144;
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(240);

    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var navigation = UseNavigation<AppRoute>();
        var state = Props.NodeState switch
        {
            LocalSendNodeState.Running => DiscoveryState.Searching,
            LocalSendNodeState.Faulted => DiscoveryState.Faulted,
            LocalSendNodeState.Created or LocalSendNodeState.Starting or LocalSendNodeState.Stopping
                or LocalSendNodeState.Stopped or LocalSendNodeState.Disposed => DiscoveryState.Stopped,
        };
        var playbackRef = UseRef<Playback?>();
        var playback = playbackRef.Current ??= new Playback
        {
            IsActive = navigation.CurrentRoute == AppRoute.Send,
        };
        playback.IsSearching = state == DiscoveryState.Searching;
        playback.ReduceMotion = reduceMotion;
        UseEffect(() => UpdatePlayback(playback), state, reduceMotion);
        UseNavigationLifecycle(
            onNavigatedTo: _ =>
            {
                playback.IsActive = true;
                UpdatePlayback(playback);
            },
            onNavigatedFrom: _ =>
            {
                playback.IsActive = false;
                UpdatePlayback(playback);
            });

        var duration = reduceMotion ? TimeSpan.Zero : TransitionDuration;
        var search = (AnimatedVisualPlayer() with { AutoPlay = false })
            .Size(Size, Size)
            .OnMountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                playback.Player = player;
                player.Source = new SearchingDevicesOverlay();
                UpdatePlayback(playback);
            })
            .OnUnmountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                player.Stop();
                if (ReferenceEquals(playback.Player, player))
                {
                    playback.Player = null;
                    playback.HasLoop = false;
                }
            });

        // All layers retain their native controls. Implicit transitions retarget
        // from the current value when another service state arrives mid-flight.
        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                Artwork("DiscoveryMonitorStopped"),
                Fade(Artwork("DiscoveryMonitor"), state != DiscoveryState.Stopped),
                Fade(Artwork("DiscoveryFaultRows"), state == DiscoveryState.Faulted),
                Marker(Border(search), state == DiscoveryState.Searching, new(72, 72, 0)),
                Marker(Artwork("DiscoveryPower"), state == DiscoveryState.Stopped, new(72, 67.5f, 0)),
                Marker(Artwork("DiscoveryError"), state == DiscoveryState.Faulted, new(102.375f, 84.9375f, 0)))
            .Size(Size, Size)
            .IsHitTestVisible(false)
            .AccessibilityHidden();

        Element Artwork(string name) => Image($"ms-appx:///Assets/Illustrations/{name}.svg")
            .Size(Size, Size).AccessibilityHidden();

        Element Fade(Element layer, bool visible) => layer
            .Opacity(visible ? 1 : 0)
            .OpacityTransition(duration);

        Element Marker(Element layer, bool visible, Vector3 center) => Fade(layer, visible)
            .CenterPoint(center)
            .Scale(visible ? 1f : 0.7f)
            .ScaleTransition(new Vector3Transition { Duration = duration });
    }

    private static void UpdatePlayback(Playback playback)
    {
        if (playback.Player is not { } player)
            return;

        if (playback.ReduceMotion)
        {
            player.Stop();
            player.SetProgress(0.25);
            playback.HasLoop = false;
        }
        else if (!playback.IsSearching || !playback.IsActive)
        {
            // Hold the live lens position while its layer shrinks and fades.
            player.Pause();
        }
        else if (playback.HasLoop)
        {
            player.Resume();
        }
        else
        {
            playback.HasLoop = true;
            _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: true);
        }
    }

    private sealed class Playback
    {
        public AnimatedVisualPlayer? Player { get; set; }
        public bool IsActive { get; set; }
        public bool IsSearching { get; set; }
        public bool ReduceMotion { get; set; }
        public bool HasLoop { get; set; }
    }

    private enum DiscoveryState { Stopped, Searching, Faulted }
}
