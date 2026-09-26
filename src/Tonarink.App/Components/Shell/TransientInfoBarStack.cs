using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed record TransientInfoBarMessage(
    Guid Id,
    string Title,
    string Message,
    InfoBarSeverity Severity);

sealed record TransientInfoBarStackProps(
    IReadOnlyList<TransientInfoBarMessage> Messages,
    Action<Guid> Remove);

/// <summary>
/// Hosts independently timed InfoBars. New messages enter from above while persistent
/// messages use Reactor's compositor-backed layout animation to make room.
/// </summary>
sealed class TransientInfoBarStack : Component<TransientInfoBarStackProps>
{
    private static readonly TimeSpan RepositionDuration = TimeSpan.FromMilliseconds(240);

    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();

        return VStack(8,
                [.. Props.Messages.Select(message =>
                    Component<TransientInfoBarItem, TransientInfoBarItemProps>(new(
                            message,
                            Props.Remove))
                        .WithKey(message.Id.ToString("N"))
                        .LayoutAnimation(reduceMotion ? TimeSpan.Zero : RepositionDuration))])
            .Margin(left: 24, top: 72, right: 24, bottom: 0)
            .MaxWidth(720)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Top)
            .IsHitTestVisible(true);
    }
}

sealed record TransientInfoBarItemProps(
    TransientInfoBarMessage Message,
    Action<Guid> Remove);

sealed class TransientInfoBarItem : Component<TransientInfoBarItemProps>
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(180);

    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var (hasEntered, setEntered) = UseState(false);
        var (isDismissing, setDismissing) = UseState(false);
        var message = Props.Message;

        UseEffect(() =>
        {
            var cancellation = new CancellationTokenSource();
            _ = RunLifetimeAsync(cancellation.Token);
            return () =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            };

            async Task RunLifetimeAsync(CancellationToken cancellationToken)
            {
                try
                {
                    // Let WinUI commit the initial off-screen visual before starting the entrance.
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                    setEntered(true);

                    await Task.Delay(VisibleDuration, cancellationToken);
                    setDismissing(true);

                    if (!reduceMotion)
                        await Task.Delay(ExitDuration, cancellationToken);
                    Props.Remove(message.Id);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The message was closed manually or the stack was unmounted.
                }
            }
        }, message.Id);

        return (InfoBar(message.Title, message.Message) with
            {
                IsOpen = true,
                IsClosable = true,
                OnClosed = () => Props.Remove(message.Id),
            })
            .Severity(message.Severity)
            .Translation(0, hasEntered ? 0 : -72, 0)
            .TranslationTransition(new()
            {
                Duration = reduceMotion ? TimeSpan.Zero : EnterDuration,
            })
            .Opacity(isDismissing || !hasEntered ? 0 : 1)
            .OpacityTransition(reduceMotion ? TimeSpan.Zero :
                isDismissing ? ExitDuration : EnterDuration);
    }
}
