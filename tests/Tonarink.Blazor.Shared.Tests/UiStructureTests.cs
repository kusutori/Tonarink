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
        Assert.Contains("BbSidebarMenuButton", layout);
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
        Assert.Contains("LucideIcon Name=\"file\"", send);
        Assert.Contains("LucideIcon Name=\"folder\"", send);
        Assert.Contains("LucideIcon Name=\"text\"", send);
        Assert.Contains("LucideIcon Name=\"clipboard\"", send);
        Assert.Contains("split-panes", send);
        Assert.Contains("T(\"ReadyToSend\")", send);
        Assert.Contains("T(\"NearbyDevices\")", send);
        Assert.Contains("_showAddress", send);
        Assert.Contains("BbDialog", send);
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
        Assert.Contains("BbSwitch", settings);
        Assert.Contains("BbSelect", settings);
    }

    [Fact]
    public void ReceiveKeepsIncomingReviewAndHistoryIsSecondary()
    {
        var receive = Read("Pages", "Receive.razor");
        Assert.Contains("/receive/", receive);
        Assert.Contains("Href=\"/history\"", receive);
        Assert.Contains("T(\"IncomingRequests\")", receive);
        Assert.Contains("BbCard", receive);
        Assert.Contains("LucideIcon", receive);
    }

    [Fact]
    public void HybridHostIncludesBlueprintThemeCss()
    {
        var host = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ShippedHost", "index.html"));
        Assert.Contains("BlazorBlueprint.Components/blazorblueprint.css", host);
        Assert.Contains("BlazorBlueprint.Components/css/themes.css", host);
        Assert.Contains("Tonarink.Blazor.Shared/theme.css", host);
        Assert.DoesNotContain("bootstrap", host);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Shipped }.Concat(parts).ToArray()));
}
