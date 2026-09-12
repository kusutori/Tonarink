using System.Net;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record FavoriteDeviceDialogProps(
    FavoriteDevice Device,
    bool IsNew,
    ElementTheme Theme,
    Action<FavoriteDevice> Save,
    Action Close);

sealed record FavoriteDeviceEdit(FavoriteDevice Device, bool IsNew);

/// <summary>Reusable editor for creating or updating a favorite device.</summary>
sealed class FavoriteDeviceDialog : Component<FavoriteDeviceDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (name, setName) = UseState(Props.Device.Name);
        var (address, setAddress) = UseState(Props.Device.Address);
        var (port, setPort) = UseState(Props.Device.Port.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        var validAddress = IPAddress.TryParse(address, out var parsedAddress);
        var validPort = int.TryParse(port, out var parsedPort)
                        && parsedPort is >= 1 and <= ushort.MaxValue;
        var canSave = !string.IsNullOrWhiteSpace(name) && validAddress && validPort;

        return (ContentDialog(
                t.Message(new("App", Props.IsNew ? "AddFavoriteTitle" : "EditFavoriteTitle")),
                VStack(12,
                        FormField(
                            TextBox(name, setName)
                                .AutomationName(t.Message(new("App", "FavoriteDeviceName"))),
                            label: t.Message(new("App", "Name")),
                            required: true),
                        FormField(
                            TextBox(address, setAddress, placeholderText: "192.168.1.72")
                                .AutomationName(t.Message(new("App", "FavoriteIpAddress"))),
                            label: t.Message(new("App", "IpAddress")),
                            required: true,
                            description: validAddress || string.IsNullOrWhiteSpace(address)
                                ? null
                                : t.Message(new("App", "InvalidIpAddress"))),
                        FormField(
                            TextBox(port, setPort, placeholderText: "53317")
                                .NumericInput()
                                .AutomationName(t.Message(new("App", "FavoritePort"))),
                            label: t.Message(new("App", "Port")),
                            required: true,
                            description: validPort || string.IsNullOrWhiteSpace(port)
                                ? null
                                : t.Message(new("App", "InvalidPort"))))
                    .MinWidth(340),
                primaryButtonText: t.Message(new("App", "Save"))) with
            {
                IsOpen = true,
                IsPrimaryButtonEnabled = canSave,
                SecondaryButtonText = t.Message(new("App", "Cancel")),
                DefaultButton = ContentDialogButton.Primary,
                OnClosed = result =>
                {
                    if (result == ContentDialogResult.Primary
                        && canSave
                        && parsedAddress is not null)
                    {
                        Props.Save(Props.Device with
                        {
                            Name = name.Trim(),
                            Address = parsedAddress.ToString(),
                            Port = parsedPort,
                        });
                    }

                    Props.Close();
                },
            }).Themed(Props.Theme);
    }
}
