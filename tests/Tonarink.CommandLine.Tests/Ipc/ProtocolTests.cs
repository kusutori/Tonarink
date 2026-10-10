using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.Cli;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class ProtocolTests
{
    [TestMethod]
    public async Task Frames_RoundTripMultipleRequestsWithoutConsumingNextFrame()
    {
        using var stream = new MemoryStream();
        var request = new CliRequest(["send", "--text", "测试\ntext"], Path.GetTempPath(), "input\n第二行", "zh-CN");
        await CliProtocol.WriteAsync(stream, request, CliJsonContext.Default.CliRequest, default);
        await CliProtocol.WriteAsync(stream, request with { Language = "en-US" }, CliJsonContext.Default.CliRequest, default);
        stream.Position = 0;
        var first = await CliProtocol.ReadAsync(stream, CliJsonContext.Default.CliRequest, default);
        Assert.AreSequenceEqual(request.Arguments, first.Arguments);
        Assert.AreEqual(request.StandardInput, first.StandardInput);
        Assert.AreEqual("zh-CN", first.Language);
        var second = await CliProtocol.ReadAsync(stream, CliJsonContext.Default.CliRequest, default);
        Assert.AreEqual("en-US", second.Language);
        Assert.AreEqual(stream.Length, stream.Position);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(4 * 1024 * 1024 + 1)]
    public async Task InvalidLength_IsRejectedBeforeAllocatingPayload(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => CliProtocol.ReadAsync(stream, CliJsonContext.Default.CliRequest, default));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(5)]
    public async Task TruncatedFrame_IsRejected(int bytes)
    {
        var frame = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 10);
        using var stream = new MemoryStream(frame, 0, bytes);
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => CliProtocol.ReadAsync(stream, CliJsonContext.Default.CliRequest, default));
    }

    [TestMethod]
    public async Task InvalidJson_IsRejected()
    {
        using var stream = new MemoryStream([1, 0, 0, 0, (byte)'!']);
        await Assert.ThrowsExactlyAsync<JsonException>(() => CliProtocol.ReadAsync(stream, CliJsonContext.Default.CliRequest, default));
    }

    [TestMethod]
    public async Task OversizedRequest_WritesNoPartialFrame()
    {
        using var stream = new MemoryStream();
        var request = new CliRequest([], Path.GetTempPath(), new string('x', 4 * 1024 * 1024));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => CliProtocol.WriteAsync(stream, request, CliJsonContext.Default.CliRequest, default));
        Assert.AreEqual(0L, stream.Length);
    }

    [TestMethod]
    public void PipeName_NormalizesDirectoryAndIsolatesProfiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "tonarink-test-profile");
        Assert.AreEqual(CliProtocol.PipeName(path), CliProtocol.PipeName(path + Path.DirectorySeparatorChar));
        Assert.AreNotEqual(CliProtocol.PipeName(path), CliProtocol.PipeName(path + "-other"));
        if (OperatingSystem.IsWindows())
            Assert.AreEqual(CliProtocol.PipeName(path), CliProtocol.PipeName(path.ToUpperInvariant()));
    }
}
