namespace Tonarink.Blazor.Shared.Tests;

public sealed class UiStructureTests
{
    private static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "Shipped");

    [Fact]
    public void PrimaryNavListsReceiveSendSettingsAndNotHistory()
    {
        var layout = Read("Layout", "MainLayout.razor");
        Assert.Contains("Href=\"/\"", layout);
        Assert.Contains("Href=\"/send\"", layout);
        Assert.Contains("Href=\"/settings\"", layout);
        Assert.Contains("FluentLayout", layout);
        Assert.Contains("FluentLayoutHamburger", layout);
        Assert.Contains("FluentNav", layout);
        Assert.Contains("FluentNavItem", layout);
        Assert.Contains("MobileBreakdownWidth=\"768\"", layout);
        Assert.DoesNotContain("Href=\"/history\"", layout);
        Assert.DoesNotContain("◉", layout);
        Assert.DoesNotContain("➤", layout);
        Assert.DoesNotContain("◷", layout);
        Assert.DoesNotContain("⚙", layout);
    }

    [Fact]
    public void SendExposesFourActionsAndTwoPanesWithSecondaryAddress()
    {
        var send = Read("Pages", "Send.razor");
        Assert.Contains("Size24.Document", send);
        Assert.Contains("Size24.Folder", send);
        Assert.Contains("Size24.TextDescription", send);
        Assert.Contains("Size24.ClipboardPaste", send);
        Assert.Contains("split-panes", send);
        Assert.Contains("T(\"ReadyToSend\")", send);
        Assert.Contains("T(\"NearbyDevices\")", send);
        Assert.Contains("_showAddress", send);
        Assert.Contains("class=\"modal-layer\"", send);
        Assert.Contains("role=\"dialog\"", send);
        Assert.DoesNotContain("▱", send);
        Assert.DoesNotContain("▣", send);
        Assert.DoesNotContain("✎", send);
    }

    [Fact]
    public void SettingsExposesAliasThemeLanguageAutoAcceptAndReceivePin()
    {
        var settings = Read("Pages", "Settings.razor");
        Assert.Contains("T(\"DeviceName\")", settings);
        Assert.Contains("T(\"Theme\")", settings);
        Assert.Contains("T(\"Language\")", settings);
        Assert.Contains("T(\"AutoAccept\")", settings);
        Assert.Contains("T(\"ReceivePin\")", settings);
        Assert.Contains("settings-row", settings);
        Assert.Contains("FluentSwitch", settings);
        Assert.Contains("FluentSelect", settings);
        Assert.Contains("FluentTextInput", settings);
    }

    [Fact]
    public void ReceiveKeepsIncomingReviewAndHistoryIsSecondary()
    {
        var receive = Read("Pages", "Receive.razor");
        Assert.Contains("/receive/", receive);
        Assert.Contains("Href=\"/history\"", receive);
        Assert.Contains("T(\"IncomingRequests\")", receive);
        Assert.Contains("AppCard", receive);
        Assert.Contains("FluentIcon", receive);
    }

    [Fact]
    public void HybridHostIncludesFluentThemeCss()
    {
        var host = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ShippedHost", "index.html"));
        Assert.Contains("Microsoft.FluentUI.AspNetCore.Components/css/reboot.css", host);
        Assert.Contains("Tonarink.Blazor.Shared/theme.css", host);
        Assert.Contains("Tonarink.Blazor.Shared/tonarink.css", host);
        Assert.DoesNotContain("BlazorBlueprint", host);
        Assert.DoesNotContain("bootstrap", host);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Shipped }.Concat(parts).ToArray()));
}
