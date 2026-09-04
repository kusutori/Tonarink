using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System.Net.Sockets;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;
using static TransferOverlayVisuals;

sealed record OutgoingTransferOverlayProps(
    OutgoingTransferViewState Transfer,
    Action Close);

sealed class OutgoingTransferOverlay : Component<OutgoingTransferOverlayProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var transfer = Props.Transfer;
        var receiverCardRef = UseRef<FrameworkElement?>(null);
        var connectedAnimationKey = DeviceConnectedKey(transfer.Receiver.Fingerprint);
        var taskbarProgress = new TaskbarTransferProgress(
            transfer.State,
            transfer.BytesTransferred,
            transfer.TotalBytes,
            transfer.Status);

        UseEffect(() => UpdateTaskbarProgress(window, taskbarProgress), taskbarProgress);
        UseEffect(() => () => ClearTaskbarProgress(window));

        var progress = transfer.TotalBytes <= 0
            ? 0
            : Math.Clamp(transfer.BytesTransferred * 100d / transfer.TotalBytes, 0, 100);
        var progressText = $"{FormatBytes(transfer.BytesTransferred)} / {FormatBytes(transfer.TotalBytes)}";

        var devices = VStack(20,
                Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                    transfer.Sender?.Alias ?? t.Message(new("App", "ThisDevice")),
                    transfer.Sender?.DeviceModel,
                    transfer.Sender?.DeviceType ?? LocalSendDeviceType.Desktop,
                    LocalDeviceNumber(transfer.Sender)))
                    .Transition(Transition.Enter(Transition.Slide(Edge.Top))),
                Icon(FontIcon("\uE74B", fontSize: 28)).AccessibilityHidden()
                    .HAlign(HorizontalAlignment.Center),
                Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                    transfer.Receiver.Alias,
                    transfer.Receiver.DeviceModel,
                    transfer.Receiver.DeviceType,
                    RemoteDeviceNumber(transfer.Receiver),
                    connectedAnimationKey,
                    AnimationRole: DeviceIdentityCardAnimationRole.Destination,
                    ElementChanged: element => receiverCardRef.Current = element)))
            .MaxWidth(720)
            .HAlign(HorizontalAlignment.Stretch);

        var status = VStack(12,
                BodyLarge(OutgoingStatus(t, transfer.State))
                    .Foreground(transfer.IsError ? Theme.SystemCritical : Theme.PrimaryText)
                    .LiveRegion(AutomationLiveSetting.Polite)
                    .HAlign(HorizontalAlignment.Center),
                TextBlock(transfer.Status)
                    .Foreground(transfer.IsError ? Theme.SystemCritical : Theme.SecondaryText)
                    .TextWrapping(TextWrapping.WrapWholeWords)
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                transfer.State is TransferState.Preparing or TransferState.WaitingForAcceptance
                    ? ProgressIndeterminate().MaxWidth(440).HAlign(HorizontalAlignment.Stretch)
                    : transfer.TotalBytes > 0
                        ? Progress(progress).MaxWidth(440).HAlign(HorizontalAlignment.Stretch)
                        : null,
                transfer.TotalBytes > 0
                    ? Caption(progressText)
                        .Foreground(Theme.SecondaryText)
                        .HAlign(HorizontalAlignment.Center)
                    : null,
                transfer.IsPending
                    ? Button(t.Message(new("App", "Cancel")), transfer.Cancel)
                        .AutomationName(t.Message(new("App", "CancelCurrentSend")))
                        .MinWidth(120)
                        .HAlign(HorizontalAlignment.Center)
                    : Button(t.Message(new("App", "Close")), () =>
                        {
                            if (receiverCardRef.Current is { } receiverCard)
                            {
                                DeviceConnectedAnimation.ReturnToSource(
                                    connectedAnimationKey,
                                    receiverCard,
                                    Props.Close);
                            }
                            else
                            {
                                Props.Close();
                            }
                        })
                        .AutomationName(t.Message(new("App", "Close")))
                        .MinWidth(120)
                        .HAlign(HorizontalAlignment.Center))
            .MaxWidth(640)
            .HAlign(HorizontalAlignment.Stretch);

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star(), GridSize.Auto],
                ScrollView(
                        VStack(28,
                            Heading(t.Message(new("App", "SendingTitle")))
                                .HeadingLevel(AutomationHeadingLevel.Level1)
                                .HAlign(HorizontalAlignment.Center),
                            Caption(transfer.ContentSummary)
                                .Foreground(Theme.SecondaryText)
                                .HAlign(HorizontalAlignment.Center),
                            devices))
                    .Padding(horizontal: 40, vertical: 32)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .Grid(row: 0),
                Border(status)
                    .Padding(horizontal: 40, vertical: 24)
                    .Grid(row: 1))
            .Transition(new FadeTransition())
            .Landmark(AutomationLandmarkType.Main);
    }

    private static string OutgoingStatus(IntlAccessor t, TransferState state) => state switch
    {
        TransferState.Preparing => t.Message(new("App", "SendingPreparing")),
        TransferState.WaitingForAcceptance => t.Message(new("App", "SendingWaiting")),
        TransferState.Transferring => t.Message(new("App", "SendingTransferring")),
        TransferState.Completed => t.Message(new("App", "TransferComplete")),
        TransferState.Cancelled => t.Message(new("App", "SendCancelled")),
        _ => t.Message(new("App", "SendFailed")),
    };
}

sealed record IncomingTransferOverlayProps(
    LocalSendNode Node,
    IncomingTransferRequest Request,
    string DownloadDirectory,
    Action<Guid> Dismiss);

sealed record IncomingTransferViewState(
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Status,
    string? Text,
    bool IsError,
    bool IsDecided)
{
    public static IncomingTransferViewState Pending(IncomingTransferRequest request, string summary) => new(
        TransferState.WaitingForAcceptance,
        0,
        request.Items.Sum(static item => item.Size),
        summary,
        InitialText(request.Items),
        IsError: false,
        IsDecided: false);

    private static string? InitialText(IReadOnlyList<IncomingItem> items) =>
        items.Count == 1 && IsText(items[0]) ? items[0].Preview : null;
}

sealed class IncomingTransferOverlay : Component<IncomingTransferOverlayProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var request = Props.Request;
        var (view, updateView) = UseReducer(IncomingTransferViewState.Pending(
            request,
            IncomingSummary(t, request.Items)));
        var (copied, setCopied) = UseState(false);
        var cancellationRef = UseRef<CancellationTokenSource?>(null);
        var taskbarProgress = new TaskbarTransferProgress(
            view.State,
            view.BytesTransferred,
            view.TotalBytes,
            view.Status);

        UseEffect(() => UpdateTaskbarProgress(window, taskbarProgress), taskbarProgress);
        UseEffect(() => () => ClearTaskbarProgress(window));
        UseEffect(() =>
        {
            WidgetAppHost.SetIncoming(new WidgetTransferInfo(
                Title: request.Items.Count == 1
                    ? request.Items[0].FileName
                    : view.Status,
                Peer: request.Sender.Alias,
                Status: view.Status,
                BytesTransferred: view.BytesTransferred,
                TotalBytes: view.TotalBytes,
                Indeterminate: view.TotalBytes <= 0
                    || view.State is TransferState.Preparing or TransferState.WaitingForAcceptance));
            return () => WidgetAppHost.SetIncoming(null);
        }, view.State, view.BytesTransferred, view.TotalBytes, view.Status, request.RequestId);

        var acceptMutation = UseMutation<bool, TransferResult>(async (_, mutationToken) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationRef.Current?.Token ?? CancellationToken.None,
                mutationToken);
            var progress = new Progress<TransferProgress>(value =>
                updateView(current => current with
                {
                    State = value.State,
                    BytesTransferred = value.BytesTransferred,
                    TotalBytes = value.TotalBytes,
                    Status = t.Message(new("App", "ReceivingContent")),
                    IsDecided = true,
                }));
            return await Props.Node.AcceptAsync(
                request.RequestId,
                new AcceptTransferOptions { DestinationDirectory = Props.DownloadDirectory },
                progress,
                linked.Token).ConfigureAwait(false);
        });

        var declineMutation = UseMutation<bool, bool>(async (_, token) =>
        {
            await Props.Node.DeclineAsync(request.RequestId, token).ConfigureAwait(false);
            return true;
        });

        var progressValue = view.TotalBytes <= 0
            ? 0
            : Math.Clamp(view.BytesTransferred * 100d / view.TotalBytes, 0, 100);
        var progressText = $"{FormatBytes(view.BytesTransferred)} / {FormatBytes(view.TotalBytes)}";
        var isPending = acceptMutation.IsPending || declineMutation.IsPending;
        var showText = request.Items.Count == 1 && IsText(request.Items[0]);
        var itemRows = request.Items.Take(5).Select(item =>
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    TextBlock(item.FileName)
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .ToolTip(item.FileName)
                        .Grid(column: 0),
                    Caption(FormatBytes(item.Size))
                        .Foreground(Theme.SecondaryText)
                        .Grid(column: 1))
                .WithKey(item.Id))
            .Cast<Element?>()
            .Append(request.Items.Count > 5
                ? Caption(t.Message(
                    new("App", "MoreItems"),
                    ("count", request.Items.Count - 5)))
                    .Foreground(Theme.SecondaryText)
                : null)
            .ToArray<Element?>();

        var sender = VStack(16,
                Border(Icon(DeviceIcon(request.Sender.DeviceType)).AccessibilityHidden())
                    .Size(88, 88)
                    .CornerRadius(44)
                    .Background(Theme.SubtleFill)
                    .HAlign(HorizontalAlignment.Center),
                Title(request.Sender.Alias)
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                HStack(8,
                    DeviceTag(RemoteDeviceNumber(request.Sender)),
                    DeviceTag(DeviceModel(t, request.Sender.DeviceModel, request.Sender.DeviceType)))
                    .HAlign(HorizontalAlignment.Center))
            .HAlign(HorizontalAlignment.Center);

        Element content = showText
            ? VStack(12,
                TextBlock(view.IsDecided
                        ? t.Message(new("App", "ReceivedMessage"))
                        : t.Message(new("App", "WantsToSendMessage")))
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                TextBox(view.Text ?? t.Message(new("App", "TextAvailableAfterAccept")), _ => { })
                    .IsReadOnly()
                    .AcceptsReturn()
                    .TextWrapping(TextWrapping.Wrap)
                    .MinHeight(120)
                    .MaxHeight(260)
                    .AutomationName(t.Message(new("App", "ReceivedTextContent"))),
                Button(copied
                        ? t.Message(new("App", "Copied"))
                        : t.Message(new("App", "Copy")), CopyText)
                    .AutomationName(t.Message(new("App", "CopyReceivedText")))
                    .IsEnabled(!string.IsNullOrEmpty(view.Text))
                    .HAlign(HorizontalAlignment.Center))
            : VStack(12,
                BodyLarge(view.Status)
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                Card(
                    VStack(8, itemRows))
                    .MaxWidth(640)
                    .HAlign(HorizontalAlignment.Stretch));

        var actions = RenderActions();

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star(), GridSize.Auto],
                ScrollView(
                        VStack(28,
                            sender,
                            content)
                        .MaxWidth(760)
                        .HAlign(HorizontalAlignment.Stretch))
                    .Padding(horizontal: 40, vertical: 40)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .Grid(row: 0),
                Border(actions)
                    .Padding(horizontal: 40, vertical: 24)
                    .Grid(row: 1))
            .Transition(Transition.Enter(new FadeTransition()))
            .Landmark(AutomationLandmarkType.Main);

        Element RenderActions()
        {
            if (!view.IsDecided)
            {
                return HStack(12,
                        Button(t.Message(new("App", "Decline")), () => _ = DeclineAsync())
                            .AutomationName(t.Message(new("App", "Decline")))
                            .Resources(static resources => resources
                                .Set("ButtonForeground", Theme.SystemCritical)
                                .Set("ButtonForegroundPointerOver", Theme.SystemCritical)
                                .Set("ButtonForegroundPressed", Theme.SystemCritical)
                                .Set("ButtonForegroundDisabled", Theme.DisabledText))
                            .IsEnabled(!isPending)
                            .MinWidth(120),
                        Button(t.Message(new("App", "Accept")), () => _ = AcceptAsync())
                            .AutomationName(t.Message(new("App", "Accept")))
                            .IsEnabled(!isPending)
                            .MinWidth(120)
                            .Resources(static resources => resources
                                .Set("ButtonBackground", Theme.Accent)
                                .Set("ButtonBackgroundPointerOver", Theme.Accent)
                                .Set("ButtonBackgroundPressed", Theme.Accent)
                                .Set("ButtonForeground", Theme.Ref("TextOnAccentFillColorPrimaryBrush"))))
                    .HAlign(HorizontalAlignment.Center);
            }

            if (acceptMutation.IsPending)
            {
                return VStack(12,
                        view.State is TransferState.Preparing or TransferState.WaitingForAcceptance
                            ? ProgressIndeterminate().MaxWidth(440).HAlign(HorizontalAlignment.Stretch)
                            : Progress(progressValue).MaxWidth(440).HAlign(HorizontalAlignment.Stretch),
                        Caption(progressText)
                            .Foreground(Theme.SecondaryText)
                            .HAlign(HorizontalAlignment.Center),
                        Button(t.Message(new("App", "Cancel")), CancelReceive)
                            .AutomationName(t.Message(new("App", "Cancel")))
                            .MinWidth(120)
                            .HAlign(HorizontalAlignment.Center))
                    .MaxWidth(520)
                    .HAlign(HorizontalAlignment.Stretch);
            }

            return VStack(8,
                    TextBlock(view.Status)
                        .Foreground(view.IsError ? Theme.SystemCritical : Theme.SecondaryText)
                        .TextAlignment(TextAlignment.Center)
                        .LiveRegion(AutomationLiveSetting.Polite),
                    Button(t.Message(new("App", "Close")), () => Props.Dismiss(request.RequestId))
                        .AutomationName(t.Message(new("App", "Close")))
                        .MinWidth(120)
                        .HAlign(HorizontalAlignment.Center))
                .HAlign(HorizontalAlignment.Center);
        }

        async Task AcceptAsync()
        {
            cancellationRef.Current?.Dispose();
            cancellationRef.Current = new CancellationTokenSource();
            updateView(current => current with
            {
                State = TransferState.Preparing,
                Status = t.Message(new("App", "ReceivingPreparing")),
                IsDecided = true,
            });
            try
            {
                var result = await acceptMutation.RunAsync(true);
                if (result.IsSuccess)
                {
                    ReceiveHistoryStore.Record(request.Sender.Alias, result);
                    AppNotificationService.Show(
                        t.Message(new("App", "NotificationReceiveCompleteTitle")),
                        request.Items.Count == 1
                            ? t.Message(
                                new("App", "NotificationReceiveCompleteOne"),
                                ("device", request.Sender.Alias))
                            : t.Message(
                                new("App", "NotificationReceiveCompleteMany"),
                                ("count", request.Items.Count),
                                ("device", request.Sender.Alias)),
                        "receive-complete");
                }
                var receivedText = showText && result.IsSuccess
                    ? await ReadReceivedTextAsync(result)
                    : view.Text;
                updateView(current => current with
                {
                    State = result.State,
                    BytesTransferred = result.BytesTransferred,
                    Status = result.State switch
                    {
                        TransferState.Completed => showText
                            ? t.Message(new("App", "TextReceived"))
                            : t.Message(new("App", "ContentSaved")),
                        TransferState.Cancelled => t.Message(new("App", "ReceiveCancelled")),
                        _ => result.Failure?.Message ?? t.Message(new("App", "ReceiveFailed")),
                    },
                    Text = receivedText ?? current.Text,
                    IsError = result.State == TransferState.Failed,
                    IsDecided = true,
                });
            }
            catch (Exception exception)
            {
                updateView(current => current with
                {
                    State = TransferState.Failed,
                    Status = exception.Message,
                    IsError = true,
                    IsDecided = true,
                });
            }
            finally
            {
                cancellationRef.Current?.Dispose();
                cancellationRef.Current = null;
            }
        }

        async Task DeclineAsync()
        {
            try
            {
                await declineMutation.RunAsync(true);
                Props.Dismiss(request.RequestId);
            }
            catch (Exception exception)
            {
                updateView(current => current with
                {
                    State = TransferState.Failed,
                    Status = exception.Message,
                    IsError = true,
                    IsDecided = true,
                });
            }
        }

        void CancelReceive() => cancellationRef.Current?.Cancel();

        void CopyText()
        {
            if (string.IsNullOrEmpty(view.Text))
                return;
            var package = new DataPackage();
            package.SetText(view.Text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            setCopied(true);
        }
    }

    private static async Task<string?> ReadReceivedTextAsync(TransferResult result)
    {
        var texts = new List<string>();
        foreach (var item in result.Items)
        {
            if (item.SavedPath is not { } path || !File.Exists(path))
                continue;
            texts.Add(await File.ReadAllTextAsync(path).ConfigureAwait(false));
        }
        return texts.Count == 0 ? null : string.Join(Environment.NewLine, texts);
    }
}

static class TransferOverlayVisuals
{
    public static string DeviceConnectedKey(string fingerprint) => $"device:{fingerprint}";

    public static void UpdateTaskbarProgress(
        ReactorWindow? window,
        TaskbarTransferProgress transfer)
    {
        if (window is null)
            return;

        var taskbar = window.TaskbarItem;
        taskbar.Description = transfer.Description;

        switch (transfer.State)
        {
            case TransferState.Preparing:
            case TransferState.WaitingForAcceptance:
                taskbar.Progress.State = TaskbarProgressState.Indeterminate;
                break;

            case TransferState.Transferring when transfer.TotalBytes > 0:
                taskbar.Progress.State = TaskbarProgressState.Normal;
                taskbar.Progress.Value = transfer.Fraction;
                break;

            case TransferState.Transferring:
                taskbar.Progress.State = TaskbarProgressState.Indeterminate;
                break;

            case TransferState.Failed:
                taskbar.Progress.State = TaskbarProgressState.Error;
                taskbar.Progress.Value = transfer.TotalBytes > 0 ? transfer.Fraction : 1;
                break;

            default:
                ClearTaskbarProgress(window);
                break;
        }
    }

    public static void ClearTaskbarProgress(ReactorWindow? window)
    {
        if (window is null)
            return;

        window.TaskbarItem.Progress.Clear();
        window.TaskbarItem.Description = null;
    }

    public static Element DeviceTag(string text) =>
        Border(Caption(text))
            .Padding(horizontal: 8, vertical: 4)
            .CornerRadius(4)
            .Background(Theme.SubtleFill);

    public static string DeviceIcon(LocalSendDeviceType type) => type switch
    {
        LocalSendDeviceType.Mobile => "Phone",
        LocalSendDeviceType.Web => "World",
        LocalSendDeviceType.Server => "World",
        _ => "Remote",
    };

    public static string DeviceTypeGlyph(LocalSendDeviceType type) => type switch
    {
        LocalSendDeviceType.Mobile => "\uE8EA",
        LocalSendDeviceType.Web => "\uE12B",
        LocalSendDeviceType.Server => "\uE968",
        LocalSendDeviceType.Headless => "\uE950",
        _ => "\uE701",
    };

    public static string DeviceModel(IntlAccessor t, string? model, LocalSendDeviceType type) =>
        string.IsNullOrWhiteSpace(model) ? type switch
        {
            LocalSendDeviceType.Mobile => t.Message(new("App", "DeviceMobile")),
            LocalSendDeviceType.Web => t.Message(new("App", "DeviceWeb")),
            LocalSendDeviceType.Headless => t.Message(new("App", "DeviceHeadless")),
            LocalSendDeviceType.Server => t.Message(new("App", "DeviceServer")),
            _ => t.Message(new("App", "DeviceDesktop")),
        } : model;

    public static string LocalDeviceNumber(LocalSendIdentity? identity)
    {
        if (identity is null || identity.Fingerprint.Length < 4)
            return "#—";
        return $"#{Convert.ToInt32(identity.Fingerprint[..4], 16) % 1000}";
    }

    public static string RemoteDeviceNumber(LocalSendDevice device)
    {
        var address = device.PreferredEndpoint?.Address;
        if (address is null)
            return "#—";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? $"#{address.GetAddressBytes()[^1]}"
            : "#—";
    }

    public static bool IsText(IncomingItem item) =>
        item.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

    public static string IncomingSummary(IntlAccessor t, IReadOnlyList<IncomingItem> items)
    {
        if (items.Count == 1 && IsText(items[0]))
            return t.Message(new("App", "IncomingTextSummary"));
        if (items.Count == 1)
            return t.Message(new("App", "IncomingFileSummary"), ("file", items[0].FileName));
        return t.Message(new("App", "IncomingItemsSummary"), ("count", items.Count));
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}

sealed record TaskbarTransferProgress(
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Description)
{
    public double Fraction => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesTransferred / (double)TotalBytes, 0, 1);
}
