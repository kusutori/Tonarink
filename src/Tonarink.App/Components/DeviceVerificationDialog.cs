using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SegmentedElement;

namespace Tonarink.Components;

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

        return (ContentDialog(
                t.Message(new("App", "VerificationTitle")),
                Props.LocalFingerprint switch
                {
                    null => TextBlock(t.Message(new("App", "IdentityLoading"))),
                    var fingerprint => VerifiedContent(fingerprint),
                },
                primaryButtonText: t.Message(new("App", "Close"))) with
            {
                IsOpen = Props.IsOpen,
                OnClosed = _ => Props.Close(),
            }).Themed(Props.Theme);

        Element VerifiedContent(string fingerprint)
        {
            var combined = DeviceVerification.CombineFingerprints(
                fingerprint,
                Props.Device.Fingerprint);
            return VStack(12,
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
                        .TextWrapping(TextWrapping.WrapWholeWords)
                        .HAlign(HorizontalAlignment.Center)
                        .Padding(top: 20),
                    Props.Device.PreferredEndpoint?.Protocol == LocalSendProtocol.Https
                        ? null
                        : InfoBar(
                                t.Message(new("App", "VerificationUnencryptedTitle")),
                                t.Message(new("App", "VerificationUnencryptedMessage"))) with
                            {
                                Severity = InfoBarSeverity.Warning,
                                IsOpen = true,
                            })
                .MinWidth(200)
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Center);
        }
    }

    private static Element VerificationIcons(string combined) =>
        (Grid(
                columns: [GridSize.Star(), GridSize.Star(), GridSize.Star(), GridSize.Star()],
                rows: [GridSize.Star(), GridSize.Star(), GridSize.Star(), GridSize.Star()],
                [
                    .. DeviceVerification.GetMaterialIconGlyphs(combined).Select((glyph, index) =>
                        TextBlock(glyph)
                            .FontFamily(MaterialIcons)
                            .FontSize(24)
                            .HAlign(HorizontalAlignment.Center)
                            .VAlign(VerticalAlignment.Center)
                            .AccessibilityHidden()
                            .Grid(row: index / 4, column: index % 4)
                            .WithKey(index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                ]) with
            {
                RowSpacing = 8,
                ColumnSpacing = 8,
            })
        .Size(224, 224)
        .HAlign(HorizontalAlignment.Center);

    private static Element VerificationText(string combined, IntlAccessor t) =>
        TextBox(combined, _ => { })
            .IsReadOnly()
            .TextWrapping()
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .AutomationName(t.Message(new("App", "VerificationText")));
}
