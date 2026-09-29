using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Pages.Send.SendDeviceResolver;

namespace Tonarink.Pages.Send;

sealed record SendAddressDialogProps(
    ElementTheme Theme,
    bool IsOpen,
    string Address,
    Action<string> SetAddress,
    string? Error,
    Action ClearError,
    bool IsResolving,
    string? RecentAddress,
    Action UseRecentAddress,
    Action<string> Confirm,
    Action Close);

sealed class SendAddressDialog : Component<SendAddressDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var hasAddress = !string.IsNullOrWhiteSpace(Props.Address);
        var hasValidFormat = hasAddress && TryParseAddress(Props.Address, out _, out _);
        var validationMessage = Props.Error
                                ?? (hasAddress && !hasValidFormat
                                    ? t.Message(new("App", "InvalidDeviceAddress"))
                                    : null);

        return (ContentDialog(
                t.Message(new("App", "EnterAddressTitle")),
                VStack(6,
                        TextBox(Props.Address, value =>
                                {
                                    Props.SetAddress(value);
                                    if (Props.Error is not null)
                                        Props.ClearError();
                                },
                                placeholderText: t.Message(new("App", "AddressPlaceholder")))
                            .AutomationName(t.Message(new("App", "DeviceAddress")))
                            .HelpText(validationMessage ?? t.Message(
                                new("App", "AddressExample"),
                                ("address", "192.168.1.100")))
                            .Required()
                            .IsEnabled(!Props.IsResolving),
                        validationMessage is not null
                            ? TextBlock(validationMessage)
                                .FontSize(14)
                                .Foreground(Theme.SystemAttention)
                                .LiveRegion(AutomationLiveSetting.Assertive)
                                .TextWrapping(TextWrapping.WrapWholeWords)
                            : Props.RecentAddress is { } recentAddress
                                ? HStack(2,
                                    TextBlock(t.Message(new("App", "RecentlyUsedAddress")))
                                        .FontSize(14)
                                        .Foreground(Theme.SecondaryText)
                                        .VAlign(VerticalAlignment.Center),
                                    HyperlinkButton(recentAddress, onClick: Props.UseRecentAddress)
                                        .Padding(0, 0)
                                        .FontSize(14)
                                        .VAlign(VerticalAlignment.Center)
                                        .AutomationName(t.Message(
                                            new("App", "UseRecentAddress"),
                                            ("address", recentAddress))))
                                : TextBlock(t.Message(
                                        new("App", "AddressExample"),
                                        ("address", "192.168.1.100")))
                                    .FontSize(14)
                                    .Foreground(Theme.SecondaryText))
                    .MinWidth(340),
                primaryButtonText: t.Message(new("App", "Confirm"))) with
        {
            IsOpen = Props.IsOpen,
            IsPrimaryButtonEnabled = !Props.IsResolving && hasValidFormat,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            OnClosed = result =>
            {
                Props.Close();
                if (result == ContentDialogResult.Primary)
                    Props.Confirm(Props.Address);
            },
        }).Themed(Props.Theme);
    }
}
