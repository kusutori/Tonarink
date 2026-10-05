using System.Net;
using System.Net.Sockets;
using LocalSendDotNet;
using Tonarink.Models;

namespace Tonarink.Pages.Settings;

// Only editable server inputs live here. Other settings remain immediately applied.
sealed record SettingsInputDraft(
    string Alias,
    string DeviceModel,
    double Port,
    double DiscoveryTimeoutMs,
    string MulticastGroup,
    string ReceivePin)
{
    public static SettingsInputDraft FromSettings(AppSettings settings) => new(
        settings.Alias, settings.DeviceModel, settings.Port, settings.DiscoveryTimeoutMs,
        settings.MulticastGroup, settings.ReceivePin);

    // Preserve edits while refreshing untouched fields after an external update.
    public SettingsInputDraft Rebase(SettingsInputDraft previous, SettingsInputDraft current) => this with
    {
        Alias = Alias == previous.Alias ? current.Alias : Alias,
        DeviceModel = DeviceModel == previous.DeviceModel ? current.DeviceModel : DeviceModel,
        Port = Port.Equals(previous.Port) ? current.Port : Port,
        DiscoveryTimeoutMs = DiscoveryTimeoutMs.Equals(previous.DiscoveryTimeoutMs)
            ? current.DiscoveryTimeoutMs : DiscoveryTimeoutMs,
        MulticastGroup = MulticastGroup == previous.MulticastGroup ? current.MulticastGroup : MulticastGroup,
        ReceivePin = ReceivePin == previous.ReceivePin ? current.ReceivePin : ReceivePin,
    };

    public bool TryValidate(bool receivePinEnabled, out string? errorKey)
    {
        errorKey = null;
        if (!double.IsFinite(Port) || Port < 1 || Port > ushort.MaxValue || Port != Math.Truncate(Port))
            errorKey = "SettingsPortInvalid";
        else if (!double.IsFinite(DiscoveryTimeoutMs) || DiscoveryTimeoutMs < 1
                 || DiscoveryTimeoutMs > 60_000 || DiscoveryTimeoutMs != Math.Truncate(DiscoveryTimeoutMs))
            errorKey = "SettingsDiscoveryTimeoutInvalid";
        else if (!string.IsNullOrWhiteSpace(MulticastGroup)
                 && (!IPAddress.TryParse(MulticastGroup.Trim(), out var address)
                     || address.AddressFamily != AddressFamily.InterNetwork
                     || address.GetAddressBytes()[0] is < 224 or > 239))
            errorKey = "SettingsMulticastInvalid";
        else if (ReceivePin.Trim().Length > 32 || (receivePinEnabled && string.IsNullOrWhiteSpace(ReceivePin)))
            errorKey = "SettingsReceivePinInvalid";

        return errorKey is null;
    }

    // Merge into the latest settings, never into the snapshot from when editing began.
    public AppSettings ApplyTo(AppSettings current) => current with
    {
        Alias = string.IsNullOrWhiteSpace(Alias) ? AppSettings.Default.Alias : Alias.Trim(),
        DeviceModel = DeviceModel.Trim(),
        Port = (int)Port,
        DiscoveryTimeoutMs = (int)DiscoveryTimeoutMs,
        MulticastGroup = string.IsNullOrWhiteSpace(MulticastGroup)
            ? LocalSendOptions.DefaultMulticastAddress.ToString() : MulticastGroup.Trim(),
        ReceivePin = ReceivePin.Trim(),
    };
}
