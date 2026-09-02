using LocalSendDotNet;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
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
        var (copyFeedback, setCopyFeedback) = UseState<(string Url, int Version)?>(null);
        var copyFeedbackVersion = UseRef(0);
        var alive = UseRef(true);
        var node = Props.Node;

        UseEffect(() => () =>
        {
            alive.Current = false;
            copyFeedbackVersion.Current++;
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
                        copyFeedback?.Url == url ? copyFeedback.Value.Version : 0,
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
                    (bool?)(pin is not null),
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
                ContentDialog(
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
                        setPinDialogOpen(false);
                        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(pinDraft))
                            return;
                        var next = pinDraft.Trim();
                        setPin(next);
                        node?.SetWebSharePin(next);
                    },
                },
                ContentDialog(
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
                },
                ContentDialog(
                    t.Message(new("App", "WebShareZoomTitle")),
                    Title(zoomUrl ?? "")
                        .TextWrapping(TextWrapping.WrapWholeWords)
                        .IsTextSelectionEnabled()
                        .AutomationName(t.Message(new("App", "WebShareZoomTitle"))),
                    primaryButtonText: t.Message(new("App", "Close"))) with
                {
                    IsOpen = zoomUrl is not null,
                    OnClosed = _ => setZoomUrl(null),
                })
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

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

            var version = ++copyFeedbackVersion.Current;
            setCopyFeedback((url, version));
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
                CopyButton(
                        copySuccessVersion,
                        t.Message(new("App", "WebShareCopy")),
                        copy)
                    .AutomationName(t.Message(new("App", "WebShareCopy")))
                    .ToolTip(t.Message(new("App", "WebShareCopy")))
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

    private static ButtonElement CopyButton(int successVersion, string name, Action copy)
    {
        var copyIcon = Icon(FontIcon("\uE8C8", fontSize: 16));
        var successIcon = Icon(FontIcon("\uE73E", fontSize: 16))
            .Opacity(0);

        if (successVersion > 0)
        {
            copyIcon = copyIcon.Keyframes("copy-feedback-out", successVersion, keyframes => keyframes
                .Duration(1433)
                .At(0.000f, opacity: 1, scale: new(1, 1, 1))
                .At(0.093f, opacity: 0, scale: new(0.273f, 0.273f, 1),
                    easing: Easing.CubicBezier(0.13f, 0, 0, 1))
                .At(0.814f, opacity: 0, scale: new(0.273f, 0.273f, 1))
                .At(0.837f, opacity: 0, scale: new(1, 1, 1))
                .At(0.907f, opacity: 0, scale: new(1, 1, 1))
                .At(1.000f, opacity: 1, scale: new(1, 1, 1), easing: Easing.EaseOut));

            successIcon = successIcon.Keyframes("copy-feedback-in", successVersion, keyframes => keyframes
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
                .At(1.000f, opacity: 0, scale: new(0.385f, 0.385f, 1)));
        }

        return Button(
                Grid(
                        columns: [GridSize.Auto],
                        rows: [GridSize.Auto],
                        copyIcon,
                        successIcon)
                    .Width(16)
                    .Height(16),
                copy)
            .SubtleButton()
            .AutomationName(name);
    }

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
