using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Components.DeviceVisuals;

namespace Tonarink.Components;

sealed record DeviceIdentityCardProps(
    string Alias,
    string? Model,
    LocalSendDeviceType Type,
    string Number,
    string? ConnectedAnimationKey = null,
    Action<FrameworkElement?>? OnClick = null,
    string? AutomationName = null,
    bool IsEnabled = true,
    double TrailingReserve = 0,
    DeviceIdentityCardAnimationRole AnimationRole = DeviceIdentityCardAnimationRole.None,
    Action<bool>? AnimationCompleted = null,
    Action<FrameworkElement?>? ElementChanged = null,
    string? SecondaryGlyph = null,
    string? SecondaryAutomationName = null,
    Action<FrameworkElement?>? OnSecondaryClick = null,
    bool IsFavorite = false);

enum DeviceIdentityCardAnimationRole
{
    None,
    Source,
    Destination,
}

sealed class DeviceIdentityCard : Component<DeviceIdentityCardProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var cardRef = UseRef<FrameworkElement?>();
        var identity = Grid(
            columns: [GridSize.Auto, GridSize.Star()],
            rows: [GridSize.Auto],
            DeviceAvatar(Props.Type).Grid(column: 0),
            VStack(8,
                    FlexRow(
                            BodyLarge(Props.Alias)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .ToolTip(Props.Alias)
                                .Flex(shrink: 1),
                            Props.IsFavorite
                                ? TextBlock("\uEC61")
                                    .FontFamily("Segoe Fluent Icons")
                                    .FontSize(16)
                                    .Foreground(Theme.AccentText)
                                    .AccessibilityHidden()
                                    .Flex(shrink: 0)
                                : null) with
                    {
                        AlignItems = FlexAlign.Center,
                        ColumnGap = 8,
                    },
                    HStack(8,
                        DeviceTag(Props.Number),
                        DeviceTag(DeviceModel(t, Props.Model, Props.Type))))
                .Margin(horizontal: 16, vertical: 0)
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 1));
        if (Props.TrailingReserve > 0)
            identity = identity.Padding(right: Props.TrailingReserve);

        Element body = Props.OnClick is null
            ? identity
            : Button(identity, () => Props.OnClick(cardRef.Current))
                .MinHeight(104)
                .Padding(16)
                .HAlign(HorizontalAlignment.Stretch)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .AutomationName(Props.AutomationName ?? Props.Alias)
                .IsEnabled(Props.IsEnabled)
                .GhostButton();

        var card = Card(body)
            .Padding(Props.OnClick is null ? 16 : 0)
            .MinHeight(104)
            .HAlign(HorizontalAlignment.Stretch)
            .WithBorder(Theme.CardStroke, 2)
            .OnMountAdd(element =>
            {
                cardRef.Current = element;
                Props.ElementChanged?.Invoke(element);

                if (Props.ConnectedAnimationKey is not { } key)
                    return;

                switch (Props.AnimationRole)
                {
                    case DeviceIdentityCardAnimationRole.Source:

                        DeviceConnectedAnimation.RegisterSource(key, element);
                        break;
                    case DeviceIdentityCardAnimationRole.Destination:

                        DeviceConnectedAnimation.StartDestinationWhenReady(
                            key,
                            element,
                            Props.AnimationCompleted);
                        break;
                    case DeviceIdentityCardAnimationRole.None:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            })
            .OnUnmountAdd(element =>
            {
                if (Props is { ConnectedAnimationKey: { } key, AnimationRole: DeviceIdentityCardAnimationRole.Source })
                {
                    DeviceConnectedAnimation.UnregisterSource(key, element);
                }

                Props.ElementChanged?.Invoke(null);
                cardRef.Current = null;
            });

        if (Props.OnSecondaryClick is null || Props.SecondaryGlyph is null)
            return card;

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Auto],
                card,
                Button(Icon(Props.SecondaryGlyph).AccessibilityHidden(), () => Props.OnSecondaryClick(cardRef.Current))
                    .AutomationName(Props.SecondaryAutomationName ?? Props.Alias)
                    .ToolTip(Props.SecondaryAutomationName ?? Props.Alias)
                    .MinWidth(48)
                    .MinHeight(48)
                    .GhostButton()
                    .HAlign(HorizontalAlignment.Right)
                    .VAlign(VerticalAlignment.Center)
                    .Margin(right: 16))
            .MinHeight(104)
            .HAlign(HorizontalAlignment.Stretch);
    }
}
