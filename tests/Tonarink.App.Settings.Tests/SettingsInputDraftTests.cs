using LocalSendDotNet;
using Tonarink.Models;
using Tonarink.Pages.Settings;

namespace Tonarink.App.Settings.Tests;

public sealed class SettingsInputDraftTests
{
    [Fact]
    public void EditingDoesNotMutatePersistedSettings()
    {
        var saved = AppSettings.Default;
        var draft = SettingsInputDraft.FromSettings(saved) with { Alias = "Edited", Port = 54321 };

        Assert.Equal(AppSettings.Default, saved);
        Assert.NotEqual(SettingsInputDraft.FromSettings(saved), draft);
        Assert.True(draft.TryValidate(false, out _));
    }

    [Fact]
    public void ApplyMergesInputsAndPreservesLatestSwitchesAndNetworkLists()
    {
        var original = AppSettings.Default;
        var draft = SettingsInputDraft.FromSettings(original) with
        {
            Alias = "  Laptop  ", DeviceModel = "  Model  ", Port = 54321,
            DiscoveryTimeoutMs = 1200, MulticastGroup = " 239.1.2.3 ", ReceivePin = " 2468 ",
        };
        var latest = original with
        {
            ThemeIndex = 2, NotificationsEnabled = false, ReceivePinEnabled = true,
            NetworkWhitelist = ["192.168.1.*"],
        };

        Assert.True(draft.TryValidate(latest.ReceivePinEnabled, out _));
        var applied = draft.ApplyTo(latest);

        Assert.Equal("Laptop", applied.Alias);
        Assert.Equal("Model", applied.DeviceModel);
        Assert.Equal(54321, applied.Port);
        Assert.Equal(1200, applied.DiscoveryTimeoutMs);
        Assert.Equal("239.1.2.3", applied.MulticastGroup);
        Assert.Equal("2468", applied.ResolvedReceivePin);
        Assert.Equal(latest.ThemeIndex, applied.ThemeIndex);
        Assert.False(applied.NotificationsEnabled);
        Assert.Same(latest.NetworkWhitelist, applied.NetworkWhitelist);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(123.5)]
    public void InvalidPortCannotBeApplied(double port)
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { Port = port };
        Assert.False(draft.TryValidate(false, out var error));
        Assert.Equal("SettingsPortInvalid", error);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(60001)]
    [InlineData(1.5)]
    public void InvalidTimeoutCannotBeApplied(double timeout)
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { DiscoveryTimeoutMs = timeout };
        Assert.False(draft.TryValidate(false, out var error));
        Assert.Equal("SettingsDiscoveryTimeoutInvalid", error);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("192.168.1.1")]
    [InlineData("240.1.2.3")]
    [InlineData("ff02::1")]
    public void InvalidMulticastCannotBeApplied(string address)
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { MulticastGroup = address };
        Assert.False(draft.TryValidate(false, out var error));
        Assert.Equal("SettingsMulticastInvalid", error);
    }

    [Fact]
    public void EmptyPinIsRejectedOnlyWhenEnabled()
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { ReceivePin = "  " };
        Assert.True(draft.TryValidate(false, out _));
        Assert.False(draft.TryValidate(true, out var error));
        Assert.Equal("SettingsReceivePinInvalid", error);
    }

    [Fact]
    public void OversizedPinIsRejected()
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { ReceivePin = new string('1', 33) };
        Assert.False(draft.TryValidate(true, out var error));
        Assert.Equal("SettingsReceivePinInvalid", error);
    }

    [Fact]
    public void BlankNameModelAndMulticastKeepDefaultResolution()
    {
        var saved = AppSettings.Default;
        var draft = SettingsInputDraft.FromSettings(saved) with
        {
            Alias = " ", DeviceModel = " ", MulticastGroup = " ",
        };
        Assert.True(draft.TryValidate(false, out _));
        var applied = draft.ApplyTo(saved);
        Assert.Equal(AppSettings.Default.ResolvedAlias, applied.ResolvedAlias);
        Assert.Equal(Environment.MachineName, applied.ResolvedDeviceModel);
        Assert.Equal(LocalSendOptions.DefaultMulticastAddress, applied.ResolvedMulticastAddress);
    }

    [Fact]
    public void RebaseRefreshesUntouchedInputsWithoutLosingDraftsOrEmptyNumbers()
    {
        var original = SettingsInputDraft.FromSettings(AppSettings.Default);
        var edited = original with { Alias = "Draft", Port = double.NaN };
        var updated = original with { Alias = "External", DeviceModel = "Updated", Port = 53318 };

        var rebased = edited.Rebase(original, updated);

        Assert.Equal("Draft", rebased.Alias);
        Assert.Equal("Updated", rebased.DeviceModel);
        Assert.True(double.IsNaN(rebased.Port));
    }
}
