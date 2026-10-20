using System.Text.Json;
using LocalSendDotNet.Protocol;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public void AnnouncementUsesV22CamelCaseShape()
    {
        var dto = new DeviceInfoDto
        {
            Alias = "测试设备",
            Version = "2.2",
            DeviceModel = null,
            DeviceType = "desktop",
            Fingerprint = new string('A', 64),
            Port = 53317,
            Protocol = "https",
            Announce = true
        };

        var json = JsonSerializer.Serialize(dto, V2Json.Options);

        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement;
        Assert.AreEqual("测试设备", payload.GetProperty("alias").GetString());
        Assert.AreEqual("2.2", payload.GetProperty("version").GetString());
        Assert.AreEqual("desktop", payload.GetProperty("deviceType").GetString());
        Assert.AreEqual(new string('A', 64), payload.GetProperty("fingerprint").GetString());
        Assert.AreEqual(53317, payload.GetProperty("port").GetInt32());
        Assert.AreEqual("https", payload.GetProperty("protocol").GetString());
        Assert.IsTrue(payload.GetProperty("announce").GetBoolean());
        Assert.IsFalse(payload.TryGetProperty("deviceModel", out _));
    }

    [TestMethod]
    public void RegisterPayloadOmitsFalseAnnounceExtension()
    {
        var dto = new DeviceInfoDto { Alias = "a", Version = "2.2", Fingerprint = "f", Port = 1, Protocol = "https" };
        var json = JsonSerializer.Serialize(dto, V2Json.Options);
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(document.RootElement.TryGetProperty("announce", out _));
        Assert.AreEqual("a", document.RootElement.GetProperty("alias").GetString());
    }

    [TestMethod]
    public void FileSizeIsDeserializedAsInt64()
    {
        const string json = """
            {"id":"x","fileName":"large.bin","size":5368709120,"fileType":"application/octet-stream"}
            """;
        var dto = JsonSerializer.Deserialize<FileDto>(json, V2Json.Options);
        Assert.AreEqual(5_368_709_120L, dto!.Size);
    }

    [TestMethod]
    [DataRow("mobile", LocalSendDeviceType.Mobile)]
    [DataRow("fridge", LocalSendDeviceType.Desktop)]
    [DataRow(null, LocalSendDeviceType.Desktop)]
    public void UnknownDeviceTypeFallsBackToDesktop(string? value, LocalSendDeviceType expected) =>
        Assert.AreEqual(expected, V2ProtocolAdapter.ParseDeviceType(value));

}
