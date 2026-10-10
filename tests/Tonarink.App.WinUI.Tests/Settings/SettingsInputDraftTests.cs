using LocalSendDotNet;
using Tonarink.Models;
using Tonarink.Pages.Settings;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class SettingsInputDraftTests
{
    [TestMethod]
    public void EditingDoesNotMutatePersistedSettings()
    {
        var saved = AppSettings.Default;
        var draft = SettingsInputDraft.FromSettings(saved) with { Alias = "Edited", Port = 54321 };

        Assert.AreEqual(AppSettings.Default, saved);
        Assert.AreNotEqual(SettingsInputDraft.FromSettings(saved), draft);
        Assert.IsTrue(draft.TryValidate(false, out _));
    }

    [TestMethod]
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
            NetworkWhitelist = (string[])["192.168.1.*"],
        };

        Assert.IsTrue(draft.TryValidate(latest.ReceivePinEnabled, out _));
        var applied = draft.ApplyTo(latest);

        Assert.AreEqual("Laptop", applied.Alias);
        Assert.AreEqual("Model", applied.DeviceModel);
        Assert.AreEqual(54321, applied.Port);
        Assert.AreEqual(1200, applied.DiscoveryTimeoutMs);
        Assert.AreEqual("239.1.2.3", applied.MulticastGroup);
        Assert.AreEqual("2468", applied.ResolvedReceivePin);
        Assert.AreEqual(latest.ThemeIndex, applied.ThemeIndex);
        Assert.IsFalse(applied.NotificationsEnabled);
        Assert.AreSame(latest.NetworkWhitelist, applied.NetworkWhitelist);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(0)]
    [DataRow(65536)]
    [DataRow(123.5)]
    public void InvalidPortCannotBeApplied(double port)
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { Port = port };
        Assert.IsFalse(draft.TryValidate(false, out var error));
        Assert.AreEqual("SettingsPortInvalid", error);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(0)]
    [DataRow(60001)]
    [DataRow(1.5)]
    public void InvalidTimeoutCannotBeApplied(double timeout)
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { DiscoveryTimeoutMs = timeout };
        Assert.IsFalse(draft.TryValidate(false, out var error));
        Assert.AreEqual("SettingsDiscoveryTimeoutInvalid", error);
    }

    [TestMethod]
    [DataRow("invalid")]
    [DataRow("192.168.1.1")]
    [DataRow("240.1.2.3")]
    [DataRow("ff02::1")]
    public void InvalidMulticastCannotBeApplied(string address)
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { MulticastGroup = address };
        Assert.IsFalse(draft.TryValidate(false, out var error));
        Assert.AreEqual("SettingsMulticastInvalid", error);
    }

    [TestMethod]
    public void EmptyPinIsRejectedOnlyWhenEnabled()
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { ReceivePin = "  " };
        Assert.IsTrue(draft.TryValidate(false, out _));
        Assert.IsFalse(draft.TryValidate(true, out var error));
        Assert.AreEqual("SettingsReceivePinInvalid", error);
    }

    [TestMethod]
    public void OversizedPinIsRejected()
    {
        var draft = SettingsInputDraft.FromSettings(AppSettings.Default) with { ReceivePin = new string('1', 33) };
        Assert.IsFalse(draft.TryValidate(true, out var error));
        Assert.AreEqual("SettingsReceivePinInvalid", error);
    }

    [TestMethod]
    public void BlankNameModelAndMulticastKeepDefaultResolution()
    {
        var saved = AppSettings.Default;
        var draft = SettingsInputDraft.FromSettings(saved) with
        {
            Alias = " ", DeviceModel = " ", MulticastGroup = " ",
        };
        Assert.IsTrue(draft.TryValidate(false, out _));
        var applied = draft.ApplyTo(saved);
        Assert.AreEqual(AppSettings.Default.ResolvedAlias, applied.ResolvedAlias);
        Assert.AreEqual(Environment.MachineName, applied.ResolvedDeviceModel);
        Assert.AreEqual(LocalSendOptions.DefaultMulticastAddress, applied.ResolvedMulticastAddress);
    }

    [TestMethod]
    public void RebaseRefreshesUntouchedInputsWithoutLosingDraftsOrEmptyNumbers()
    {
        var original = SettingsInputDraft.FromSettings(AppSettings.Default);
        var edited = original with { Alias = "Draft", Port = double.NaN };
        var updated = original with { Alias = "External", DeviceModel = "Updated", Port = 53318 };

        var rebased = edited.Rebase(original, updated);

        Assert.AreEqual("Draft", rebased.Alias);
        Assert.AreEqual("Updated", rebased.DeviceModel);
        Assert.IsTrue(double.IsNaN(rebased.Port));
    }
}
