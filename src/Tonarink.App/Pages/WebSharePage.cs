using LocalSendDotNet;
using System.Collections.Immutable;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages;

sealed record WebSharePageProps(
    LocalSendNode? Node,
    AppRuntimeState Runtime,
    AppSettings Settings,
    Action<bool?> SetHttpsOverride,
    WebShareMode Mode);

sealed class WebSharePage : Component<WebSharePageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var items = WebShareLaunch.Items;
        var (share, setShare) = UseState(WebShareState.Inactive);
        var (autoAccept, setAutoAccept) = UseState(false);
        var (pin, setPin) = UseState<string?>(null);
        var (pinDraft, setPinDraft) = UseState(RandomPin());
        var (pinDialogOpen, setPinDialogOpen) = UseState(false);
        var (encrypted, setEncrypted) = UseState(
            Props is { Mode: WebShareMode.Send, Runtime.Identity.Protocol: LocalSendProtocol.Https });
        var (qrUrl, setQrUrl) = UseState<string?>(null);
        var (zoomUrl, setZoomUrl) = UseState<string?>(null);
        var (copyFeedbackVersions, updateCopyFeedbackVersions) =
            UseReducer(ImmutableDictionary<string, int>.Empty);
        var nextCopyFeedbackVersion = UseRef(0);
        var shareSource = UseRef<WindowsShareSource?>();
        var alive = UseRef(true);
        var node = Props.Node;
        var window = UseWindow();
        var dialogTheme = AppTheme.ToElementTheme(Props.Settings.ThemeIndex);

        UseEffect(() => () =>
        {
            alive.Current = false;
            nextCopyFeedbackVersion.Current++;
            shareSource.Current?.Dispose();
            shareSource.Current = null;
        });

        UseNavigationLifecycle(
            onNavigatedTo: _ =>
            {
                if (Props.Mode == WebShareMode.Receive)
                    Props.SetHttpsOverride(encrypted);
            },
            onNavigatedFrom: _ =>
            {
                shareSource.Current?.Dispose();
                shareSource.Current = null;
                node?.StopWebShare();
                Props.SetHttpsOverride(null);
            });

        UseEffect(() =>
        {
            if (node is null || Props.Runtime.NodeState != LocalSendNodeState.Running ||
                (Props.Mode == WebShareMode.Send && items.Count == 0))
                return () => { };

            _ = Props.Mode == WebShareMode.Send
                ? node.StartWebShareAsync(items, new WebShareOptions
                {
                    AutoAccept = autoAccept,
                    Pin = pin,
                    UiCulture = AppLocale.Resolve(Props.Settings.LanguageIndex),
                })
                : node.StartWebReceiveAsync(new WebShareOptions
                {
                    AutoAccept = autoAccept,
                    Pin = pin,
                    UiCulture = AppLocale.Resolve(Props.Settings.LanguageIndex),
                });
            var watch = new CancellationTokenSource();
            _ = WatchAsync(watch.Token);
            return () =>
            {
                watch.Cancel();
                watch.Dispose();
                node.StopWebShare();
            };

            async Task WatchAsync(CancellationToken cancellationToken)
            {
                try
                {
                    setShare(node.GetWebShare());
                    await foreach (var next in node.WatchWebShareAsync(cancellationToken).ConfigureAwait(true))
                        setShare(next);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when leaving the page or restarting the web-share service.
                }
            }
        }, node, Props.Runtime.NodeState, Props.Mode);

        var https = encrypted;
        var port = Props.Runtime.Identity?.Port ?? Props.Settings.Port;
        var urls = AppNetworkAddresses.ListWebShareIpv4(Props.Settings)
            .Select(address => $"{(https ? "https" : "http")}://{address}:{port}")
            .ToArray();
        if (urls.Length == 0)
            urls = [$"{(https ? "https" : "http")}://127.0.0.1:{port}"];

        Element requestBody = share.Requests switch
        {
            [] => TextBlock(t.Message(new("App", "WebShareNoRequests")))
                .Foreground(Theme.SecondaryText),
            _ => VStack(8, [
                .. share.Requests.Select(request =>
                    RequestCard(t, request, node).WithKey(request.SessionId))
            ]),
        };

        return ScrollView(
                VStack(24,
                        TextBlock(t.Message(new("App", "WebShareOpenLink")))
                            .SemiBold(),
                        VStack(8, [
                            .. urls.Select(url =>
                                LinkBar(
                                    t,
                                    url,
                                    CollectionExtensions.GetValueOrDefault(copyFeedbackVersions, url, 0),
                                    () => _ = CopyWithFeedbackAsync(url),
                                    ShowQr,
                                    setZoomUrl,
                                    () => ShareLink(url)).WithKey(url))
                        ]),
                        VStack(8,
                            BodyStrong(t.Message(new("App", "WebShareRequests"))),
                            requestBody),
                        CheckBox(
                            (bool?)encrypted,
                            value =>
                            {
                                setEncrypted(value);
                                Props.SetHttpsOverride(value);
                            },
                            t.Message(new("App", "WebShareEncryption"))),
                        encrypted
                            ? TextBlock(t.Message(new("App", "WebShareEncryptionHint")))
                                .Foreground(Theme.SystemCaution)
                                .TextWrapping(TextWrapping.WrapWholeWords)
                            : null,
                        CheckBox(
                            (bool?)autoAccept,
                            value =>
                            {
                                setAutoAccept(value);
                                node?.SetWebShareAutoAccept(value);
                            },
                            t.Message(new("App", "WebShareAutoAccept"))),
                        CheckBox(
                            (bool?)(pin is not null || pinDialogOpen),
                            value =>
                            {
                                if (value)
                                {
                                    setPinDraft(RandomPin());
                                    setPinDialogOpen(true);
                                }
                                else
                                {
                                    setPin(null);
                                    node?.SetWebSharePin(null);
                                }
                            },
                            t.Message(new("App", "WebShareRequirePin"))),
                        pin is null
                            ? null
                            : TextBlock(t.Message(new("App", "WebSharePinHint"), ("pin", pin)))
                                .Foreground(Theme.SystemCaution),
                        (ContentDialog(
                                t.Message(new("App", "WebSharePinTitle")),
                                TextBox(pinDraft, setPinDraft)
                                    .AutomationName(t.Message(new("App", "WebSharePinTitle")))
                                    .Required(),
                                primaryButtonText: t.Message(new("App", "Confirm"))) with
                        {
                            IsOpen = pinDialogOpen,
                            SecondaryButtonText = t.Message(new("App", "Cancel")),
                            DefaultButton = ContentDialogButton.Primary,
                            OnClosed = result =>
                            {
                                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(pinDraft))
                                {
                                    var next = pinDraft.Trim();
                                    setPin(next);
                                    node?.SetWebSharePin(next);
                                }

                                setPinDialogOpen(false);
                            },
                        }).Themed(dialogTheme),
                        (ContentDialog(
                                t.Message(new("App", "WebShareQrTitle")),
                                qrUrl is null
                                    ? Empty()
                                    : VStack(12,
                                        QrCodeCanvas.Render(
                                            qrUrl,
                                            t.Message(new("App", "WebShareQrTitle"))),
                                        TextBlock(qrUrl)
                                            .TextWrapping(TextWrapping.WrapWholeWords)
                                            .IsTextSelectionEnabled()),
                                primaryButtonText: t.Message(new("App", "Close"))) with
                        {
                            IsOpen = qrUrl is not null,
                            OnClosed = _ => setQrUrl(null),
                        }).Themed(dialogTheme),
                        (ContentDialog(
                                t.Message(new("App", "WebShareZoomTitle")),
                                Title(zoomUrl ?? "")
                                    .TextWrapping(TextWrapping.WrapWholeWords)
                                    .IsTextSelectionEnabled()
                                    .AutomationName(t.Message(new("App", "WebShareZoomTitle"))),
                                primaryButtonText: t.Message(new("App", "Close"))) with
                        {
                            IsOpen = zoomUrl is not null,
                            OnClosed = _ => setZoomUrl(null),
                        }).Themed(dialogTheme))
                    .Padding(AppLayout.PagePadding))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(t.Message(new(
                "App",
                Props.Mode == WebShareMode.Send ? "WebShareTitle" : "WebReceiveTitle")))
            .Landmark(AutomationLandmarkType.Main);

        void ShowQr(string url)
        {
            setQrUrl(url);
        }

        void ShareLink(string url)
        {
            try
            {
                var nativeWindow = window?.NativeWindow
                                   ?? throw new InvalidOperationException(
                                       t.Message(new("App", "WindowUnavailable")));
                (shareSource.Current ??= new WindowsShareSource(
                    nativeWindow,
                    t.Message(new("App", "WebShareNoLinkAvailable")))).ShareLink(
                    url,
                    t.Message(new("App", Props.Mode == WebShareMode.Receive
                        ? "WebReceiveTitle"
                        : "WebShareTitle")));
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not open the Windows share sheet for a web link", exception);
            }
        }

        async Task CopyWithFeedbackAsync(string url)
        {
            if (!await CopyAsync(url).ConfigureAwait(true) || !alive.Current)
                return;

            var version = ++nextCopyFeedbackVersion.Current;
            updateCopyFeedbackVersions(current => current.SetItem(url, version));
        }
    }

    private static Element LinkBar(
        IntlAccessor t,
        string url,
        int copySuccessVersion,
        Action copy,
        Action<string> showQr,
        Action<string?> setZoom,
        Action share) =>
        Border(
                Grid(
                        columns: [GridSize.Star(), GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Auto],
                        rows: [GridSize.Auto],
                        TextBlock(url)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .IsTextSelectionEnabled()
                            .VAlign(VerticalAlignment.Center)
                            .ToolTip(url)
                            .Grid(column: 0),
                        AnimatedButtons.CopyFeedback(
                                copySuccessVersion,
                                t.Message(new("App", "WebShareCopy")),
                                copy,
                                successAnnouncement: t.Message(new("App", "Copied")))
                            .MinWidth(40)
                            .MinHeight(40)
                            .Grid(column: 1),
                        IconButton("\uED14", t.Message(new("App", "WebShareQr")), () => showQr(url))
                            .Grid(column: 2),
                        IconButton("\uE7F4", t.Message(new("App", "WebShareZoom")), () => setZoom(url))
                            .Grid(column: 3),
                        IconButton("\uE72D", t.Message(new("App", "WebShareSystemShare")), share)
                            .Grid(column: 4)) with
                {
                    ColumnSpacing = 4,
                })
            .Padding(horizontal: 16, vertical: 8)
            .CornerRadius(8)
            .Background(Theme.SubtleFill);

    private static Element RequestCard(IntlAccessor t, WebShareRequest request, LocalSendNode? node) =>
        Border(
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    VStack(4,
                            TextBlock(request.DeviceInfo)
                                .Foreground(request.Pending ? Theme.SystemCaution : Theme.PrimaryText),
                            Caption(request.Ip).Foreground(Theme.SecondaryText))
                        .Grid(column: 0),
                    (request.Pending
                        ? (Element)HStack(4,
                            Button(Icon("Cancel").AccessibilityHidden(), () => node?.DeclineWebShareRequest(request.SessionId))
                                .SubtleButton()
                                .AutomationName(t.Message(new("App", "Decline")))
                                .MinWidth(40)
                                .MinHeight(40),
                            Button(Icon("Accept").AccessibilityHidden(), () => node?.AcceptWebShareRequest(request.SessionId))
                                .SubtleButton()
                                .AutomationName(t.Message(new("App", "Accept")))
                                .MinWidth(40)
                                .MinHeight(40))
                        : Caption(t.Message(new("App", "WebShareAccepted")))
                            .Foreground(Theme.SecondaryText)
                            .VAlign(VerticalAlignment.Center))
                    .Grid(column: 1)))
            .Padding(12)
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke);

    private static Element IconButton(string glyph, string name, Action onClick) =>
        Button(Icon(glyph).AccessibilityHidden(), onClick)
            .SubtleButton()
            .MinWidth(40)
            .MinHeight(40)
            .AutomationName(name)
            .ToolTip(name)
            .MinWidth(40)
            .MinHeight(40);

    private static async Task<bool> CopyAsync(string url)
    {
        const int maximumAttempts = 3;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(url);

                if (!Clipboard.SetContentWithOptions(package, new ClipboardContentOptions()))
                {
                    if (attempt < maximumAttempts)
                        await Task.Delay(50).ConfigureAwait(true);
                    continue;
                }

                try
                {
                    Clipboard.Flush();
                }
                catch (Exception exception)
                {
                    // The text is already available for this app lifetime. Flush only
                    // keeps it available after exit and may fail if another process
                    // briefly locks the clipboard.
                    AppDiagnostics.Report("Could not persist clipboard text after copying a share link", exception);
                }

                return true;
            }
            catch (Exception exception) when (attempt < maximumAttempts)
            {
                AppDiagnostics.Report($"Clipboard copy attempt {attempt} failed", exception);
                await Task.Delay(50).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not copy the share link", exception);
                return false;
            }
        }

        return false;
    }

    private static string RandomPin()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        Span<char> chars = stackalloc char[6];
        Random.Shared.GetItems(alphabet.AsSpan(), chars);
        return new string(chars);
    }
}
