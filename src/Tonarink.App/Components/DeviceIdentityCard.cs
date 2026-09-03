using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static TransferOverlayVisuals;

sealed record DeviceIdentityCardProps(
    string Alias,
    string? Model,
    LocalSendDeviceType Type,
    string Number,
    string? ConnectedAnimationKey = null,
    Action? OnClick = null,
    string? AutomationName = null,
    bool IsEnabled = true);

sealed class DeviceIdentityCard : Component<DeviceIdentityCardProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var identity = Grid(
            columns: [GridSize.Auto, GridSize.Star()],
            rows: [GridSize.Auto],
            Border(Icon(DeviceIcon(Props.Type)).AccessibilityHidden())
                .Size(64, 64)
                .CornerRadius(32)
                .Background(Theme.SubtleFill)
                .Grid(column: 0),
            VStack(8,
                BodyLarge(Props.Alias)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .ToolTip(Props.Alias),
                HStack(8,
                    DeviceTag(Props.Number),
                    DeviceTag(DeviceModel(t, Props.Model, Props.Type))))
                .Margin(horizontal: 16, vertical: 0)
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 1));

        Element body = Props.OnClick is null
            ? identity
            : Button(identity, Props.OnClick)
                .MinHeight(104)
                .Padding(16)
                .HAlign(HorizontalAlignment.Stretch)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .AutomationName(Props.AutomationName ?? Props.Alias)
                .IsEnabled(Props.IsEnabled)
                .Resources(static resources => resources
                    .Set("ButtonBackground", Theme.Ref("SubtleFillColorTransparentBrush"))
                    .Set("ButtonBackgroundPointerOver", Theme.Ref("SubtleFillColorSecondaryBrush"))
                    .Set("ButtonBackgroundPressed", Theme.Ref("SubtleFillColorTertiaryBrush"))
                    .Set("ButtonBorderBrush", Theme.Ref("SubtleFillColorTransparentBrush")));

        var card = Card(body)
            .Padding(Props.OnClick is null ? 16 : 0)
            .MinHeight(104)
            .HAlign(HorizontalAlignment.Stretch);

        return Props.ConnectedAnimationKey is null
            ? card
            : card.ConnectedAnimation(Props.ConnectedAnimationKey);
    }
}
