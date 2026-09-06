using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.Toolkit.SegmentedElement;

sealed record DeviceVerificationDialogProps(
    LocalSendDevice Device,
    string? LocalFingerprint,
    ElementTheme Theme,
    bool IsOpen,
    Action Close);

sealed class DeviceVerificationDialog : Component<DeviceVerificationDialogProps>
{
    private static readonly FontFamily MaterialIcons = new(
        "ms-appx:///Assets/MaterialIcons-Regular.ttf#Material Icons");

    public override Element Render()
    {
        var t = UseIntl();
        var (mode, setMode) = UseState(0);
        var modes = UseMemo(() => new object[]
        {
            t.Message(new("App", "VerificationIcons")),
            t.Message(new("App", "VerificationText")),
        }, t.Locale);

        Element content;
        if (Props.LocalFingerprint is null)
        {
            content = TextBlock(t.Message(new("App", "IdentityLoading")));
        }
        else
        {
            var combined = DeviceVerification.CombineFingerprints(
                Props.LocalFingerprint,
                Props.Device.Fingerprint);
            content = VStack(12,
                Segmented(
                    selectedIndex: mode,
                    onSelectedIndexChanged: setMode,
                    items: modes)
                    .HAlign(HorizontalAlignment.Stretch),
                Grid(
                        columns: [GridSize.Star()],
                        rows: [GridSize.Star()],
                        mode == 0 ? VerificationIcons(combined) : VerificationText(combined, t))
                    .Height(224)
                    .HAlign(HorizontalAlignment.Stretch),
                TextBlock(t.Message(new("App", "VerificationCompareHint")))
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.WrapWholeWords),
                Props.Device.PreferredEndpoint?.Protocol == LocalSendProtocol.Https
                    ? null
                    : InfoBar(
                        t.Message(new("App", "VerificationUnencryptedTitle")),
                        t.Message(new("App", "VerificationUnencryptedMessage"))) with
                    {
                        Severity = InfoBarSeverity.Warning,
                        IsOpen = true,
                    })
                .MinWidth(384)
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Center);
        }

        return (ContentDialog(
            t.Message(new("App", "VerificationTitle")),
            content,
            primaryButtonText: t.Message(new("App", "Close"))) with
        {
            IsOpen = Props.IsOpen,
            OnClosed = _ => Props.Close(),
        }).Set(dialog => dialog.RequestedTheme = Props.Theme);
    }

    private static Element VerificationIcons(string combined) =>
        (Grid(
            columns: [GridSize.Star(), GridSize.Star(), GridSize.Star(), GridSize.Star()],
            rows: [GridSize.Star(), GridSize.Star(), GridSize.Star(), GridSize.Star()],
            DeviceVerification.GetMaterialIconGlyphs(combined).Select((glyph, index) =>
                TextBlock(glyph)
                    .FontFamily(MaterialIcons)
                    .FontSize(24)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
                    .AccessibilityHidden()
                    .Grid(row: index / 4, column: index % 4)
                    .WithKey(index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray<Element?>()) with
        {
            RowSpacing = 8,
            ColumnSpacing = 8,
        })
        .Size(224, 224)
        .HAlign(HorizontalAlignment.Center);

    private static Element VerificationText(string combined, IntlAccessor t) =>
        TextBox(combined, _ => { })
            .IsReadOnly(true)
            .TextWrapping(TextWrapping.Wrap)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .AutomationName(t.Message(new("App", "VerificationText")));
}
