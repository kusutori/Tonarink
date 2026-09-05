using LocalSendDotNet;
using System.Collections.Immutable;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Tonarink.Components.Animations;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;

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
            Props.Mode == WebShareMode.Send && Props.Runtime.Identity?.Protocol == LocalSendProtocol.Https);
        var (qrPath, setQrPath) = UseState<string?>(null);
        var (qrUrl, setQrUrl) = UseState<string?>(null);
        var (zoomUrl, setZoomUrl) = UseState<string?>(null);
        var (copyFeedbackVersions, updateCopyFeedbackVersions) =
            UseReducer(ImmutableDictionary<string, int>.Empty);
        var nextCopyFeedbackVersion = UseRef(0);
        var alive = UseRef(true);
        var node = Props.Node;
        var dialogTheme = Props.Settings.ThemeIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        UseEffect(() => () =>
        {
            alive.Current = false;
            nextCopyFeedbackVersion.Current++;
        });

        UseNavigationLifecycle(
            onNavigatedTo: _ =>
            {
                if (Props.Mode == WebShareMode.Receive)
                    Props.SetHttpsOverride(encrypted);
            },
            onNavigatedFrom: _ =>
            {
                node?.StopWebShare();
                Props.SetHttpsOverride(null);
            });

        UseEffect(() =>
        {
            if (node is null || Props.Runtime.NodeState != LocalSendNodeState.Running ||
                (Props.Mode == WebShareMode.Send && items.Count == 0))
                return () => { };

            _ = Props.Mode == WebShareMode.Send
                ? node.StartWebShareAsync(items, new WebShareOptions { AutoAccept = autoAccept, Pin = pin })
                : node.StartWebReceiveAsync(new WebShareOptions { AutoAccept = autoAccept, Pin = pin });
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

        Element requestBody = share.Requests.Count == 0
            ? TextBlock(t.Message(new("App", "WebShareNoRequests")))
                .Foreground(Theme.SecondaryText)
            : VStack(8, share.Requests.Select(request =>
                RequestCard(t, request, node).WithKey(request.SessionId)).ToArray<Element?>());

        return ScrollView(
            VStack(24,
                Heading(t.Message(new("App", Props.Mode == WebShareMode.Receive ? "WebReceiveTitle" : "WebShareTitle")))
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                TextBlock(t.Message(new("App", "WebShareOpenLink")))
                    .SemiBold(),
                VStack(8, urls.Select(url =>
                    LinkBar(
                        t,
                        url,
                        copyFeedbackVersions.TryGetValue(url, out var version) ? version : 0,
                        () => _ = CopyWithFeedbackAsync(url),
                        ShowQr,
                        setZoomUrl).WithKey(url)).ToArray<Element?>()),
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
                        .AutomationName(t.Message(new("App", "WebSharePinTitle"))),
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
                }).Set(dialog => ApplyDialogTheme(dialog, dialogTheme)),
                (ContentDialog(
                    t.Message(new("App", "WebShareQrTitle")),
                    qrPath is null
                        ? ProgressRing()
                        : VStack(12,
                            Image(qrPath)
                                .Size(240, 240)
                                .HAlign(HorizontalAlignment.Center)
                                .AutomationName(t.Message(new("App", "WebShareQrTitle"))),
                            TextBlock(qrUrl ?? "")
                                .TextWrapping(TextWrapping.WrapWholeWords)
                                .IsTextSelectionEnabled(true)),
                    primaryButtonText: t.Message(new("App", "Close"))) with
                {
                    IsOpen = qrUrl is not null,
                    OnClosed = _ =>
                    {
                        setQrUrl(null);
                        setQrPath(null);
                    },
                }).Set(dialog => ApplyDialogTheme(dialog, dialogTheme)),
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
                }).Set(dialog => ApplyDialogTheme(dialog, dialogTheme)))
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        static void ApplyDialogTheme(ContentDialog dialog, ElementTheme theme) =>
            dialog.RequestedTheme = theme;

        void ShowQr(string url)
        {
            setQrUrl(url);
            setQrPath(null);
            _ = WriteQrAsync(url);
        }

        async Task WriteQrAsync(string url)
        {
            try
            {
                var path = await QrPng.WriteAsync(url).ConfigureAwait(true);
                setQrPath(path);
            }
            catch
            {
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
        Action<string?> setZoom) =>
        Border(
            Grid(
                columns: [GridSize.Star(), GridSize.Auto, GridSize.Auto, GridSize.Auto],
                rows: [GridSize.Auto],
                TextBlock(url)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .IsTextSelectionEnabled(true)
                    .VAlign(VerticalAlignment.Center)
                    .ToolTip(url)
                    .Grid(column: 0),
                AnimatedButtons.CopyFeedback(
                        copySuccessVersion,
                        t.Message(new("App", "WebShareCopy")),
                        copy)
                    .MinWidth(40)
                    .MinHeight(40)
                    .Grid(column: 1),
                IconButton("\uED14", t.Message(new("App", "WebShareQr")), () => showQr(url))
                    .Grid(column: 2),
                IconButton("\uE7F4", t.Message(new("App", "WebShareZoom")), () => setZoom(url))
                    .Grid(column: 3)) with
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
                        Button(Icon("Cancel"), () => node?.DeclineWebShareRequest(request.SessionId))
                            .SubtleButton()
                            .AutomationName(t.Message(new("App", "Decline")))
                            .MinWidth(40)
                            .MinHeight(40),
                        Button(Icon("Accept"), () => node?.AcceptWebShareRequest(request.SessionId))
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
            .WithBorder(Theme.CardStroke, 1);

    private static Element IconButton(string glyph, string name, Action onClick) =>
        Button(Icon(glyph), onClick)
            .SubtleButton()
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
                catch
                {
                    // The text is already available for this app lifetime. Flush only
                    // keeps it available after exit and may fail if another process
                    // briefly locks the clipboard.
                }

                return true;
            }
            catch when (attempt < maximumAttempts)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }
            catch
            {
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
