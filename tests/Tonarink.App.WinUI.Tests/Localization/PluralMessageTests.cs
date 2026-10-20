using Microsoft.UI.Reactor.Localization;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("Localization")]
public sealed class PluralMessageTests
{
    // Exercise the shipped .resw files through the same accessor as the App,
    // rather than a test-only resource dictionary or plural implementation.
    [TestMethod]
    [DataRow("en-US", 0, new string[]
    {
        "0 items", "Wants to send you 0 items.", "Added 0 items", "0 more…",
        "0 pending requests", "0 items selected · 2 KB", "0 of 0 files selected",
        "Received 0 items from Peer.", "Sent 0 items to Peer."
    })]
    [DataRow("en-US", 1, new string[]
    {
        "1 item", "Wants to send you 1 item.", "Added 1 item", "1 more…",
        "1 pending request", "1 item selected · 2 KB", "1 of 1 file selected",
        "Received an item from Peer.", "Sent an item to Peer."
    })]
    [DataRow("en-US", 2, new string[]
    {
        "2 items", "Wants to send you 2 items.", "Added 2 items", "2 more…",
        "2 pending requests", "2 items selected · 2 KB", "2 of 2 files selected",
        "Received 2 items from Peer.", "Sent 2 items to Peer."
    })]
    [DataRow("zh-CN", 0, new string[]
    {
        "0 个项目", "想要发送给你 0 个项目。", "已添加 0 项", "另外 0 项…",
        "0 个待处理请求", "已选择 0 项 · 2 KB", "已选择 0/0 个文件",
        "已接收来自 Peer 的 0 个项目。", "已向 Peer 发送 0 个项目。"
    })]
    [DataRow("zh-CN", 1, new string[]
    {
        "1 个项目", "想要发送给你 1 个项目。", "已添加 1 项", "另外 1 项…",
        "1 个待处理请求", "已选择 1 项 · 2 KB", "已选择 1/1 个文件",
        "已接收来自 Peer 的 1 个项目。", "已向 Peer 发送 1 个项目。"
    })]
    [DataRow("zh-CN", 2, new string[]
    {
        "2 个项目", "想要发送给你 2 个项目。", "已添加 2 项", "另外 2 项…",
        "2 个待处理请求", "已选择 2 项 · 2 KB", "已选择 2/2 个文件",
        "已接收来自 Peer 的 2 个项目。", "已向 Peer 发送 2 个项目。"
    })]
    public void ShippedEnglishAndChineseMessagesFormatZeroSingularAndPlural(
        string locale, int count, string[] expected)
    {
        var intl = new IntlAccessor(locale, global::Tonarink.AppShell.Resources, new MessageCache());
        string[] keys =
        [
            "ContentItems", "IncomingItemsSummary", "ItemsAdded", "MoreItems", "PendingRequests",
            "SelectedItems", "SelectedIncomingFiles", "NotificationReceiveComplete", "NotificationSendComplete"
        ];
        var actual = keys.Select(key => intl.Message(new("App", key),
            ("count", count), ("selected", count), ("size", "2 KB"), ("device", "Peer"))).ToArray();

        CollectionAssert.AreEqual(expected, actual);
    }
}
