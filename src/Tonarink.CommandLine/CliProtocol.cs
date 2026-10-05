using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Tonarink.Cli;

sealed record CliRequest(string[] Arguments, string WorkingDirectory, string? StandardInput);
sealed record CliResponse(string Type, int ExitCode, string? Message, JsonElement? Data = null);
sealed record CliDevice(string Name, string Fingerprint, string Type, string[] Addresses, string ProtocolVersion);
sealed record CliStatus(bool AppRunning, string State, string? Name, string? Fingerprint, int? Port, string? Protocol, string? Error, string? DiscoveryWarning);
sealed record CliSetting(string Key, string Value);
sealed record CliIncoming(Guid Id, Guid TransferId, string Sender, string Fingerprint, string[] Files, long Bytes);
sealed record CliProgress(Guid TransferId, string Direction, string State, long BytesTransferred, long TotalBytes);
sealed record CliTransfer(Guid TransferId, string State, string[] Files, string? Error = null);
sealed record CliHistory(Guid Id, string FileName, string Path, long Size, string Sender, DateTimeOffset ReceivedAt);
sealed record CliFavorite(string Fingerprint, string Name, string Address, int Port, string? DeviceType = null);
sealed record CliWebShare(bool Active, string Mode, bool AutoAccept, bool PinRequired, string[] Files, string[] PendingRequests, string[] Links);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliRequest))]
[JsonSerializable(typeof(CliResponse))]
[JsonSerializable(typeof(CliDevice[]))]
[JsonSerializable(typeof(CliStatus))]
[JsonSerializable(typeof(CliSetting[]))]
[JsonSerializable(typeof(CliIncoming[]))]
[JsonSerializable(typeof(CliProgress))]
[JsonSerializable(typeof(CliTransfer))]
[JsonSerializable(typeof(CliHistory[]))]
[JsonSerializable(typeof(CliFavorite[]))]
[JsonSerializable(typeof(CliWebShare))]
sealed partial class CliJsonContext : JsonSerializerContext;

static class CliProtocol
{
    public const string BackgroundArgument = "--cli-background";
    private const int MaximumFrameBytes = 4 * 1024 * 1024;

    // Package-local storage isolates sideload, Store and unpackaged development
    // instances. CurrentUserOnly on both pipe ends restricts access to this user.
    public static string PipeName(string dataDirectory)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        return "Tonarink.Cli." + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory)))[..24];
    }

    public static async Task WriteAsync<T>(Stream stream, T value, JsonTypeInfo<T> type, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type);
        if (bytes.Length > MaximumFrameBytes)
            throw new InvalidDataException("Command or response exceeds the 4 MiB limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, JsonTypeInfo<T> type, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Invalid CLI message length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("Empty CLI message.");
    }

    public static CliResponse Result<T>(string message, T data, JsonTypeInfo<T> type, int exitCode = 0) =>
        new("result", exitCode, message, JsonSerializer.SerializeToElement(data, type));

    public static CliResponse Success(string message) => new("result", 0, message);
    public static CliResponse Error(string message, int exitCode = 1) => new("result", exitCode, message);
}

sealed class CliException(string message, int exitCode = 2) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
