using System.Net;
using System.Net.Sockets;
using LocalSendDotNet;

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
    public SettingsInputDraft Rebase(SettingsInputDraft previous, SettingsInputDraft current) => new(
        Alias: Alias == previous.Alias ? current.Alias : Alias,
        DeviceModel: DeviceModel == previous.DeviceModel ? current.DeviceModel : DeviceModel,
        Port: Port.Equals(previous.Port) ? current.Port : Port,
        DiscoveryTimeoutMs: DiscoveryTimeoutMs.Equals(previous.DiscoveryTimeoutMs)
            ? current.DiscoveryTimeoutMs
            : DiscoveryTimeoutMs,
        MulticastGroup: MulticastGroup == previous.MulticastGroup ? current.MulticastGroup : MulticastGroup,
        ReceivePin: ReceivePin == previous.ReceivePin ? current.ReceivePin : ReceivePin);

    public bool TryValidate(bool receivePinEnabled, out string? errorKey)
    {
        errorKey = null;
        if (!double.IsInteger(Port) || Port is < 1 or > ushort.MaxValue)
            errorKey = "SettingsPortInvalid";
        else if (!double.IsInteger(DiscoveryTimeoutMs) || DiscoveryTimeoutMs is < 1 or > 60_000)
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
            ? LocalSendOptions.DefaultMulticastAddress.ToString()
            : MulticastGroup.Trim(),
        ReceivePin = ReceivePin.Trim(),
    };
}
